#!/usr/bin/env python3
"""Alternate production libraries and write strategies; retain every verified process."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--config", required=True, type=Path)
parser.add_argument("--output", required=True, type=Path)
args = parser.parse_args()
config = json.loads(args.config.read_text())
args.output.mkdir(parents=True, exist_ok=True)
manifest = {"configuration": config, "driver_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), "runs": []}
for group in config["groups"]:
    for callers in group["callers"]:
        for repeat in range(group.get("rounds", 4)):
            cases = group["cases"] if repeat % 2 == 0 else list(reversed(group["cases"]))
            for version, mode, batch, linger in cases:
                name = f'{group["name"]}-{callers}-{repeat}-{version}-{mode}-{batch}-{linger}'
                definition = config["versions"][version]
                command = ["dotnet", definition["runner"], "bench", mode, str(callers), str(batch), str(linger),
                           str(group.get("warmup", 3)), str(group.get("seconds", 5)), str(args.output / "databases")]
                environment = os.environ.copy()
                environment["DOTNET_TieredCompilation"] = "0"
                result = subprocess.run(command, text=True, capture_output=True, env=environment, timeout=180)
                (args.output / (name + ".jsonl")).write_text(result.stdout)
                (args.output / (name + ".stderr")).write_text(result.stderr)
                manifest["runs"].append({"name": name, "command": command, "exit": result.returncode,
                                         "revision": definition["revision"], "finished": datetime.datetime.now(datetime.timezone.utc).isoformat()})
                (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2))
                if result.returncode:
                    raise SystemExit(f"{name} failed; see retained output")
                row = json.loads(result.stdout)
                if not row["verified"]:
                    raise SystemExit(f"{name} was not verified")
                print(f'{name}: {row["opsPerSecond"]:.1f} ops/s; p99={row["p99us"]:.0f} us', flush=True)
