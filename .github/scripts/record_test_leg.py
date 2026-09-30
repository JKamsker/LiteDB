"""Record candidate/build provenance and discovery for a filtered native CI leg."""
import argparse
import json
import os
import re
import subprocess
from pathlib import Path


def selected(name, expression):
    """Evaluate the deliberately small FQN filter language used by native legs."""
    if not expression:
        return True
    for alternative in expression.split("|"):
        clauses = alternative.split("&")
        matches = []
        for clause in clauses:
            match = re.fullmatch(r"FullyQualifiedName(!?~)(.+)", clause.strip())
            if not match:
                raise ValueError(f"Unsupported native evidence filter clause: {clause}")
            operator, value = match.groups()
            matches.append((value in name) == (operator == "~"))
        if all(matches):
            return True
    return False


def record(root, results, discovery, expression, result, framework, runtime, architecture):
    names = discovery.read_text(encoding="utf-8-sig").splitlines()
    names = sorted({name.strip() for name in names if name.strip()
                    and (selected(name.strip(), expression) or "TestHost_Tests" in name)})
    if not names:
        raise ValueError("Native test discovery produced no selected tests")
    # This file travels with the binaries downloaded from the corresponding build
    # job. Never substitute HEAD when the binary provenance is missing.
    build = json.loads((root / "LiteDB.Tests/bin/Release/build-evidence.json").read_text(encoding="utf-8-sig"))
    revision = lambda ref: subprocess.check_output(["git", "-C", str(root), "rev-parse", ref], text=True).strip()
    leg = {
        "schemaVersion": 1, "job": os.environ.get("GITHUB_JOB"),
        "matrix": json.loads(os.environ.get("LITEDB_EVIDENCE_MATRIX", "null")),
        "sha": revision("HEAD"), "tree": revision("HEAD^{tree}"), "buildSha": build["sha"],
        "runtimeMajor": runtime, "framework": framework, "architecture": architecture,
        "format": "trx", "discovery": "discovered-tests.txt", "partitions": {"native": result},
    }
    results.mkdir(parents=True, exist_ok=True)
    (results / "discovered-tests.txt").write_text("\n".join(names) + "\n", encoding="utf-8")
    (results / "evidence-leg.json").write_text(json.dumps(leg, indent=2) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--results", type=Path, required=True)
    parser.add_argument("--discovery", type=Path, required=True)
    parser.add_argument("--filter", default="")
    parser.add_argument("--result", required=True)
    parser.add_argument("--framework", required=True)
    parser.add_argument("--runtime", type=int, required=True)
    parser.add_argument("--architecture", required=True)
    args = parser.parse_args()
    record(args.root, args.results, args.discovery, args.filter, args.result,
           args.framework, args.runtime, args.architecture)


if __name__ == "__main__":
    main()
