#!/usr/bin/env python3
"""Execute a precompiled parent consumer and a behavioral known-bad/fixed comparison."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--parent-dll", type=Path, required=True)
parser.add_argument("--candidate-dll", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
results = []


def run(name, command, expected=0, contains=None):
    result = subprocess.run(command, capture_output=True, text=True, timeout=120)
    output = result.stdout + result.stderr
    (args.output / (name + ".log")).write_text(output)
    results.append({"name": name, "command": command, "exit": result.returncode})
    if result.returncode != expected or (contains and contains not in output):
        raise RuntimeError(f"{name}: unexpected result {result.returncode}; see {args.output}")


source = Path(__file__).resolve().parent.parent / "tools" / "TransactionHandleValidation"
with tempfile.TemporaryDirectory(prefix="litedb-handle-api-") as temporary:
    root = Path(temporary)
    shutil.copytree(source, root / "source", ignore=shutil.ignore_patterns("bin", "obj"))
    project = str(root / "source" / "TransactionHandleValidation.csproj")
    parent = root / "parent"
    candidate = root / "candidate"
    run("build-parent", ["dotnet", "build", project, "-c", "Release",
        f"-p:LibraryPath={args.parent_dll.resolve()}", "-o", str(parent)])
    shutil.copytree(parent, candidate)
    shutil.copy2(args.candidate_dll, candidate / "LiteDB.dll")
    for version, directory in (("parent", parent), ("candidate", candidate)):
        binary = str(directory / "TransactionHandleValidation.dll")
        run(version + "-binary", ["dotnet", binary, "binary"], contains="PASS precompiled")
        run(version + "-regression", ["dotnet", binary, "error"],
            expected=17 if version == "parent" else 0,
            contains="primary=rollback cleanup error" if version == "parent" else "PASS original error preserved")
    build = ["dotnet", "build", project, "-c", "Release",
        f"-p:LibraryPath={args.candidate_dll.resolve()}",
        f"-p:BaseIntermediateOutputPath={root / 'warnings-obj'}/", "-p:TreatWarningsAsErrors=true",
        "-o", str(root / "warnings")]
    run("warnings-as-errors", build, expected=1, contains="error CS0618")
    run("targeted-warning-exception", build + ["-p:WarningsNotAsErrors=CS0618"], contains="Build succeeded")

manifest = {"parent_sha256": hashlib.sha256(args.parent_dll.read_bytes()).hexdigest(),
    "candidate_sha256": hashlib.sha256(args.candidate_dll.read_bytes()).hexdigest(), "results": results}
(args.output / "manifest.json").write_text(json.dumps(manifest, indent=2))
print("PASS: old interface implementations/consumer, capability refusal, known-bad regression, CS0618 migration")
