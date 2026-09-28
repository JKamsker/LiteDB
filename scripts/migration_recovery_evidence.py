"""Retain migration child diagnostics without moving the exercised storage volume."""
import hashlib
import json
import os
import pathlib
import platform
import shutil
import subprocess
import tempfile
import time


class RecoveryEvidence:
    def __init__(self, command, artifacts):
        self.command = list(map(str, command))
        self.artifacts = pathlib.Path(artifacts)
        # Keep the actual database on the original system-temp volume.
        self.directory = pathlib.Path(tempfile.mkdtemp(prefix="litedb-migration-recovery-"))
        self.children = []

    def run(self, *args, check=True, timeout=90):
        command = self.command + list(map(str, args))
        number = len(self.children)
        record = {"command": command, "timeout_seconds": timeout,
                  "started": time.time(), "status": "running"}
        self.children.append(record)
        stdout = self.directory / f"child-{number:02d}.stdout.txt"
        stderr = self.directory / f"child-{number:02d}.stderr.txt"
        record.update(stdout=stdout.name, stderr=stderr.name)
        started = time.monotonic()
        try:
            # File-backed capture survives timeout and does not wait for pipe EOF.
            # subprocess.run kills and reaps the child before raising on timeout.
            with stdout.open("wb") as out, stderr.open("wb") as err:
                result = subprocess.run(command, stdout=out, stderr=err, timeout=timeout)
            record.update(status="exited", returncode=result.returncode)
            result.stdout = stdout.read_text(errors="replace")
            result.stderr = stderr.read_text(errors="replace")
            if check:
                result.check_returncode()
            return result
        except BaseException as error:
            record["status"] = "timeout" if isinstance(error, subprocess.TimeoutExpired) else "failed"
            record["exception"] = repr(error)
            raise
        finally:
            record["elapsed_seconds"] = time.monotonic() - started
            try:
                record["reached"] = [line for line in stdout.read_text(errors="replace").splitlines()
                                     if line.startswith(("STAGE:", "FAULT:"))]
            except OSError as error:
                record["capture_error"] = repr(error)

    def finish(self, error=None):
        if error is None:
            shutil.rmtree(self.directory)
            return None
        metadata = {"original_directory": str(self.directory), "platform": platform.platform(),
                    "commit": os.environ.get("GITHUB_SHA"), "error": repr(error),
                    "children": self.children, "files": [], "runner_files": []}
        for argument in self.command:
            binary = pathlib.Path(argument)
            if binary.is_file():
                metadata["runner_files"].append({"path": str(binary.resolve()),
                                                "sha256": hashlib.sha256(binary.read_bytes()).hexdigest()})
                for dependency in (binary.parent / "LiteDB.dll", binary.with_suffix(".runtimeconfig.json")):
                    if dependency.is_file():
                        metadata["runner_files"].append({"path": str(dependency.resolve()),
                                                        "sha256": hashlib.sha256(dependency.read_bytes()).hexdigest()})
        for path in sorted(self.directory.rglob("*")):
            if path.is_file():
                metadata["files"].append({"path": str(path.relative_to(self.directory)),
                                          "bytes": path.stat().st_size,
                                          "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
        (self.directory / "evidence.json").write_text(json.dumps(metadata, indent=2) + "\n")
        destination = self.artifacts / self.directory.name
        # Preserve the original failure tree too; a failed copy must never delete it.
        print(f"Migration failure evidence retained at {self.directory}", flush=True)
        self.artifacts.mkdir(parents=True, exist_ok=True)
        shutil.copytree(self.directory, destination)
        print(f"Migration failure artifact copy: {destination}", flush=True)
        return destination


def assert_fault(result, mode, stage):
    marker = f"FAULT:{mode}:{stage}"
    if result.returncode == 0 or marker not in result.stdout:
        raise RuntimeError(f"Fault was not reached: {marker}\n{result.stdout}\n{result.stderr}")
    if mode == "io" and result.returncode != 17:
        raise RuntimeError(f"Unexpected I/O failure exit: {result.returncode}\n{result.stderr}")
