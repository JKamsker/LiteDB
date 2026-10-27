"""Offline analysis only; safe to run locally against downloaded runner artifacts."""
from collections import Counter, defaultdict
import json
import os
from pathlib import Path
import re
import statistics
import sys

SCENARIOS = {"Bulk": "bulk_insert_100k", "Point": "indexed_point_10k", "Scan": "full_scan_3x100k",
             "Update": "update_10k_one_tx", "Delete": "delete_10k_one_tx", "Single": "single_insert_5k_tx",
             "Checkpoint": "checkpoint_100k", "Open": "open_close_20x"}
# Exclusive partition, first matching category wins. Inclusive method totals are separate.
CATEGORIES = {
    "checkpoint": ["Checkpoint", "HeaderJournal", "CheckpointVerification"],
    "compact codec": ["CompactDocument", "DocumentStorageCodec", "SchemaCatalog", "CommitSchemas", "ObserveShape"],
    "WAL/checksums/padding": ["WriteLog", "FlushConfirmedLog", "WalChecksum", "WalPadding", "ChecksummedWalStream", "PageChecksum"],
    "transaction/safepoints": ["TransactionMonitor", "Safepoint", "TransactionRegistry", "TransactionService", "TransactionGate"],
    "page cache/ownership": ["MemoryCache", "PageBuffer", "Frame", "DiskReader", "Snapshot"],
    "query executor": ["QueryExecutor", "QueryPipe", "QueryOptimization", "IndexService", "IndexScan"],
    "mapping/BSON": ["BsonMapper", "BsonReader", "BsonWriter", "BufferReader", "BufferWriter"],
}


def scenario_for(names):
    for method, scenario in SCENARIOS.items():
        if any(re.search(r"Measurement[.:+]" + method + r"\b", name) for name in names):
            return scenario
    return None


def attribute(names, weight, totals, categories, methods):
    if weight <= 0:
        return
    scenario = scenario_for(names)
    if scenario is None:
        return
    totals[scenario] += weight
    category = next((key for key, patterns in CATEGORIES.items()
                     if any(pattern in name for name in names for pattern in patterns)), "other")
    categories[scenario][category] += weight
    for name in set(names):
        if "LiteDB" in name and "Ab5021" not in name:
            methods[scenario][name] += weight


def finish_profile(totals, categories, methods):
    return {scenario: dict(totalWeight=total,
                          partition=[dict(category=k, weight=v, percent=100 * v / total)
                                     for k, v in categories[scenario].most_common()],
                          inclusiveMethods=[dict(method=k, weight=v, percent=100 * v / total)
                                            for k, v in methods[scenario].most_common(40)])
            for scenario, total in totals.items() if total > 0}


def speedscope(path):
    data = json.loads(path.read_text())
    frames = [frame["name"] for frame in data["shared"]["frames"]]
    totals, categories, methods = Counter(), defaultdict(Counter), defaultdict(Counter)
    units = set()
    for profile in data["profiles"]:
        units.add(profile["unit"])
        if profile["type"] == "sampled":
            samples = profile["samples"]
            weights = profile.get("weights", [1] * len(samples))
            for sample, weight in zip(samples, weights):
                attribute([frames[i] for i in sample], weight, totals, categories, methods)
        elif profile["type"] == "evented":
            stack, previous = [], profile["startValue"]
            for event in profile["events"]:
                attribute([frames[i] for i in stack], event["at"] - previous, totals, categories, methods)
                previous = event["at"]
                if event["type"] == "O":
                    stack.append(event["frame"])
                elif stack:
                    stack.pop()
    return dict(units=sorted(units), kind="sampled thread time, includes off-CPU", scenarios=finish_profile(totals, categories, methods))


def perf_stacks(path):
    totals, categories, methods = Counter(), defaultdict(Counter), defaultdict(Counter)
    for block in re.split(r"\n\s*\n", path.read_text(errors="replace")):
        names = [line.strip() for line in block.splitlines()[1:] if line.startswith("\t") or line.startswith(" ")]
        attribute(names, 1, totals, categories, methods)
    return dict(kind="kernel cpu-clock samples; unresolved JIT stacks excluded", scenarios=finish_profile(totals, categories, methods))


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


def analyze(output):
    raw = [json.loads(path.read_text()) for path in sorted(output.glob("round-*.json"))]
    groups = defaultdict(list)
    for result in raw:
        for sample in result["samples"]:
            groups[(sample["scenario"], result["config"])].append(sample)
    summary = []
    for (scenario, config), samples in groups.items():
        if len(samples) != 6:
            raise ValueError(f"Expected six independent rounds: {scenario}/{config}: {len(samples)}")
        baseline = "write-base-fast" if config.startswith("write-") else "v5-no-checkpoint" if config.endswith("no-checkpoint") else "v5"
        reference = "write-base-fast" if config.startswith("write-") else "dev-fast"
        median = lambda field, rows=samples: statistics.median(row[field] for row in rows)
        ms = median("milliseconds")
        ref = median("milliseconds", groups[(scenario, reference)])
        summary.append(dict(scenario=scenario, config=config, medianMs=ms,
                            minMs=min(row["milliseconds"] for row in samples),
                            maxMs=max(row["milliseconds"] for row in samples),
                            cpuMs=median("cpuMs"), allocatedBytes=median("allocatedBytes"),
                            gc=[median("gen0"), median("gen1"), median("gen2")],
                            reference=reference, savedVsReferenceMs=ref - ms,
                            savedVsReferencePercent=100 * (ref - ms) / ref,
                            baseline=baseline, ratio=ms / median("milliseconds", groups[(scenario, baseline)])))
    (output / "summary.json").write_text(json.dumps(summary, indent=2))
    profiles = {path.name: speedscope(path) for path in sorted(output.glob("trace-*.speedscope.json"))}
    kernel = {path.name: perf_stacks(path) for path in sorted(output.glob("perf-*.stacks"))}
    syscalls = {path.name: syscall_sections(path) for path in sorted(output.glob("strace-detail-*.txt"))}
    for name, data in [("sample-attribution", profiles), ("kernel-cpu-attribution", kernel), ("syscall-attribution", syscalls)]:
        (output / (name + ".json")).write_text(json.dumps(data, indent=2))
    lines = ["# P1-D hosted attribution", "",
             "Six interleaved, fresh-process rounds; Release, hooks off, tiering off. All commits opt out of per-commit device sync.",
             "Ablations change one control except Throughput and the explicitly combined control. Savings are not additive.",
             "CPU is measured process CPU during each scenario; allocation/GC deltas include background activity.",
             "EventPipe shares are sampled thread time including off-CPU, not exact CPU or causal savings.",
             "strace -c totals include setup/disposal; syscall-attribution.json uses measured markers only.",
             "Summed syscall durations are perturbed thread time, not wall-time savings. Profiles run separately from timing.", ""]
    for scenario in dict.fromkeys(row["scenario"] for row in summary):
        lines += ["## " + scenario, "", "| Configuration | median ms (range) | CPU ms | allocated MiB | G0/G1/G2 | ratio vs baseline | saved vs reference ms |",
                  "|---|---:|---:|---:|---|---:|---:|"]
        rows = sorted((row for row in summary if row["scenario"] == scenario), key=lambda row: -row["savedVsReferenceMs"])
        for row in rows:
            lines.append(f"| {row['config']} | {row['medianMs']:.3f} ({row['minMs']:.3f}–{row['maxMs']:.3f}) | {row['cpuMs']:.1f} | {row['allocatedBytes']/1048576:.2f} | {row['gc']} | {row['ratio']:.3f} | {row['savedVsReferenceMs']:.3f} |")
        for path, profile in profiles.items():
            if scenario in profile["scenarios"]:
                partition = profile["scenarios"][scenario]["partition"]
                lines += ["", path + " sampled partition: " + "; ".join(f"{r['category']} {r['percent']:.1f}%" for r in partition)]
    lines += ["", "write-proto vs write-base isolates the existing prototype as a bundle, not its individual changes.",
              "Cache-size toggles isolate capacity, not the CPU cost of the cache implementation. No checksum/durability bypass is applied.",
              "Missing profiles/kernel symbols and empty scenario partitions are evidence gaps, not zero cost."]
    markdown = "\n".join(lines) + "\n"
    (output / "attribution.md").write_text(markdown)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        Path(os.environ["GITHUB_STEP_SUMMARY"]).write_text(markdown)


if __name__ == "__main__":
    analyze(Path(sys.argv[1]))
