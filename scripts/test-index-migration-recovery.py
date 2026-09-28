#!/usr/bin/env python3
"""Interrupt real file migrations at durable promotion, WAL, commit and checkpoint."""
import pathlib
import shutil
import subprocess
from migration_recovery_evidence import RecoveryEvidence, assert_fault

root = pathlib.Path(__file__).resolve().parent.parent
project = root / "tools/IndexMigrationRecovery/IndexMigrationRecovery.csproj"
runner = project.parent / "bin/Release/net8.0/IndexMigrationRecovery.dll"


def exercise(evidence):
    directory = evidence.directory
    run = evidence.run
    for encryption in ("plain", "encrypted"):
        original = directory / (encryption + ".db")
        run("create", original, encryption)
        for mode in ("crash", "io"):
            for stage in ("promotion", "wal", "commit", "checkpoint"):
                target = directory / f"{encryption}-{mode}-{stage}.db"
                shutil.copyfile(original, target)
                result = run("fault", target, encryption, mode, stage, check=False)
                assert_fault(result, mode, stage)
                verified = run("verify", target, encryption, check=False)
                if verified.returncode:
                    raise RuntimeError(f"Recovery failed: {encryption}/{mode}/{stage}\n{verified.stdout}\n{verified.stderr}")
                print(f"PASS {encryption}: {mode} during {stage}", flush=True)


def main():
    subprocess.run(["dotnet", "build", str(project), "-c", "Release", "-p:TestingEnabled=true"], check=True)
    evidence = RecoveryEvidence(["dotnet", runner], root / "artifacts_temp/index-migration-recovery")
    try:
        exercise(evidence)
    except BaseException as error:
        try:
            evidence.finish(error)
        except Exception as retention_error:
            print(f"Evidence copying failed: {retention_error}; original files remain at {evidence.directory}", flush=True)
        raise
    else:
        evidence.finish()


if __name__ == "__main__":
    main()
