"""Offline diagnostic analysis; no product execution."""
import json
from pathlib import Path
import re

from collections import defaultdict


def syscall_sections(path):
    current = None
    sections, pending = defaultdict(lambda: defaultdict(lambda: dict(calls=0, errors=0, summedSeconds=0.0))), {}
    for line in path.read_text(errors="replace").splitlines():
        marker = re.search(r"P1D_(BEGIN|END) (\w+)", line)
        if marker:
            current = marker[2] if marker[1] == "BEGIN" else None
            continue
        match = re.match(r"\s*(\d+)\s+\d+\.\d+\s+(.*)", line)
        if not match:
            continue
        pid, call = match.groups()
        if "<unfinished ...>" in call:
            pending[pid] = (current, call.split("<unfinished")[0])
            continue
        selected = current
        if call.startswith("<..."):
            selected, prefix = pending.pop(pid, (None, ""))
            resumed = re.search(r"resumed>(.*)", call)
            call = prefix + (resumed[1] if resumed else "")
        name = re.match(r"(\w+)\(", call)
        if not selected or not name:
            continue
        duration = re.search(r"<([0-9.]+)>$", call)
        target = "wal" if "-log.db" in call else "data" if ".db" in call else "other"
        stats = sections[selected][name[1] + ":" + target]
        stats["calls"] += 1
        stats["errors"] += bool(re.search(r"= -1\b", call))
        stats["summedSeconds"] += float(duration[1]) if duration else 0
    return sections



def completed_calls(lines):
    pending = {}
    current = None
    for line in lines:
        marker = re.search(r"P1D_(BEGIN|END) (\w+)", line)
        if marker:
            current = marker[2] if marker[1] == "BEGIN" else None
        match = re.match(r"\s*(\d+)\s+\d+\.\d+\s+(.*)", line)
        if not match: continue
        pid, call = match.groups()
        if "<unfinished ...>" in call:
            pending[pid] = (current, call.split("<unfinished")[0])
            continue
        selected = current
        if call.startswith("<..."):
            selected, prefix = pending.pop(pid, (None, ""))
            resumed = re.search(r"resumed>(.*)", call)
            call = prefix + (resumed[1] if resumed else "")
        yield selected, call


def audit_eof(path):
    eof, transitions = {}, {k: 0 for k in ["extend", "equal", "shrink", "unknown"]}
    for scenario, call in completed_calls(path.read_text(errors="replace").splitlines()):
        if "-log.db>" not in call: continue
        descriptor = re.search(r"\d+<([^>]+-log\.db)>", call)
        if not descriptor: continue
        name = descriptor[1]
        # Only successful size-changing operations; return count handles short writes.
        write = re.match(r"pwrite64\(.*?,\s*(\d+),\s*(\d+)\)\s*=\s*(\d+)\s+<", call)
        if write:
            eof[name] = max(eof.get(name, 0), int(write[2]) + int(write[3]))
        truncate = re.match(r"ftruncate\(.*?,\s*(\d+)\)\s*=\s*0\s+<", call)
        if truncate:
            length = int(truncate[1])
            if scenario == "single_insert_5k_tx":
                old = eof.get(name)
                kind = "unknown" if old is None else "extend" if length > old else "shrink" if length < old else "equal"
                transitions[kind] += 1
            eof[name] = length
    return dict(transitions=transitions, assumption="all EOF-changing writes/truncations captured, no mmap mutation")


def diagnostics(output):
    report = {}
    for path in output.glob("strace-detail-*.txt"):
        sections = syscall_sections(path)
        selected = sections.get("single_insert_5k_tx", {})
        if not selected: raise ValueError("Missing measured syscall window: " + str(path))
        for stats in selected.values():
            stats["callsPerCommit"] = stats["calls"] / 5000
            stats["perturbedSummedMsPerCommit"] = stats["summedSeconds"] * 1000 / 5000
        if any(v["calls"] for k, v in selected.items() if k.startswith(("fsync:", "fdatasync:"))):
            raise ValueError("Unexpected CP0 device sync: " + str(path))
        report[path.name] = dict(syscalls=selected, eof=audit_eof(path))
    (output / "syscall-per-commit.json").write_text(json.dumps(report, indent=2))
