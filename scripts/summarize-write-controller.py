#!/usr/bin/env python3
"""Summarize all write-controller processes and matched throughput ratios."""
import argparse
import json
from pathlib import Path
import statistics

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("directory", type=Path)
args = parser.parse_args()
manifest = json.loads((args.directory / "manifest.json").read_text())
packed = {}
if (args.directory / "raw.jsonl").exists():
    for line in (args.directory / "raw.jsonl").read_text().splitlines():
        row = json.loads(line)
        packed[row.pop("run")] = row
groups = {}
for run in manifest["runs"]:
    if run["exit"]:
        raise ValueError("Failed run: " + run["name"])
    row = packed[run["name"]] if packed else json.loads((args.directory / (run["name"] + ".jsonl")).read_text())
    if not row["verified"]:
        raise ValueError("Unverified run: " + run["name"])
    name = run["name"]
    group = "write-baseline" if name.startswith("write-baseline-") else "strategies"
    suffix = name.removeprefix(group + "-").split("-")
    callers, repeat, version = int(suffix[0]), int(suffix[1]), suffix[2]
    key = (group, callers, version, row["mode"], row["batch"], row["linger"])
    groups.setdefault(key, []).append({"name": name, "repeat": repeat, **row})
summary = []
for key, runs in groups.items():
    result = dict(zip(("group", "callers", "version", "mode", "batch", "linger"), key))
    result["processes"] = len(runs)
    for column in ("opsPerSecond", "p50us", "p95us", "p99us", "clientBytesPerOp", "cpuMs", "serverCpuMs", "peakWal", "drainMs"):
        values = [row[column] for row in runs]
        result[column] = {"median": statistics.median(values), "min": min(values), "max": max(values)}
    if key[0] == "write-baseline" and key[2] == "head":
        baseline = {row["repeat"]: row for row in groups[(key[0], key[1], "dev", *key[3:])]}
    elif key[0] == "strategies":
        baseline = {row["repeat"]: row for row in groups[(key[0], key[1], "head", "direct", 1, 0)]}
    else:
        baseline = {}
    ratios = [row["opsPerSecond"] / baseline[row["repeat"]]["opsPerSecond"] for row in runs if row["repeat"] in baseline]
    result["pairedRatios"] = ratios
    result["medianPairedRatio"] = statistics.median(ratios) if ratios else None
    result["runs"] = runs
    summary.append(result)
print(json.dumps(summary, indent=2))
