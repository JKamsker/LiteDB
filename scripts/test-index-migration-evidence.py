"""Exercise failure retention with real children; never shorten the migration budget."""
import json
import pathlib
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from migration_recovery_evidence import RecoveryEvidence, assert_fault


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.artifacts = tempfile.TemporaryDirectory()
        self.evidence = RecoveryEvidence([sys.executable, "-c"], self.artifacts.name)
        self.fixture = {"data.db": b"data", "data-log.db": b"wal",
                        "recovery/marker": b"marker", "sidecar": b"coordination"}
        for name, data in self.fixture.items():
            p = self.evidence.directory / name
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_bytes(data)

    def tearDown(self):
        shutil.rmtree(self.evidence.directory, ignore_errors=True)
        self.artifacts.cleanup()

    def retain(self, error):
        artifact = self.evidence.finish(error)
        for name, data in self.fixture.items():
            self.assertEqual(data, (artifact / name).read_bytes())
            self.assertEqual(data, (self.evidence.directory / name).read_bytes())
        metadata = json.loads((artifact / "evidence.json").read_text())
        self.assertEqual(str(self.evidence.directory), metadata["original_directory"])
        return artifact, metadata["children"][-1]

    def test_timeout_retains_output_reached_stage_and_reaps_child(self):
        # The locked file can only be exclusively acquired after the child exits.
        lock = self.evidence.directory / "child.lock"
        code = ("import os, pathlib, sys, time; "
                f"f=open({str(lock)!r}, 'w'); f.write('x'); f.flush(); "
                "exec(\"if os.name == 'nt':\\n import msvcrt; f.seek(0); msvcrt.locking(f.fileno(), msvcrt.LK_NBLCK, 1)\\n"
                "else:\\n import fcntl; fcntl.flock(f, fcntl.LOCK_EX | fcntl.LOCK_NB)\"); "
                "print('STAGE:crash:commit:write', flush=True); "
                "print('child stderr', file=sys.stderr, flush=True); time.sleep(60)")
        with self.assertRaises(subprocess.TimeoutExpired) as caught:
            self.evidence.run(code, timeout=5)
        artifact, record = self.retain(caught.exception)
        self.assertEqual("timeout", record["status"])
        self.assertEqual(["STAGE:crash:commit:write"], record["reached"])
        self.assertIn("child stderr", (artifact / record["stderr"]).read_text())
        with lock.open("r+") as f:
            if sys.platform == "win32":
                import msvcrt
                msvcrt.locking(f.fileno(), msvcrt.LK_NBLCK, 1)
                msvcrt.locking(f.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                import fcntl
                fcntl.flock(f, fcntl.LOCK_EX | fcntl.LOCK_NB)

    def test_nonzero_child_retains_output(self):
        with self.assertRaises(subprocess.CalledProcessError) as caught:
            self.evidence.run("import sys; print('failed'); sys.exit(23)")
        artifact, record = self.retain(caught.exception)
        self.assertEqual(23, record["returncode"])
        self.assertIn("failed", (artifact / record["stdout"]).read_text())

    def test_missing_fault_marker_retains_semantic_failure(self):
        result = self.evidence.run("import sys; print('started'); sys.exit(17)", check=False)
        with self.assertRaisesRegex(RuntimeError, "Fault was not reached") as caught:
            assert_fault(result, "io", "wal")
        _, record = self.retain(caught.exception)
        self.assertEqual([], record["reached"])

    def test_expected_fault_and_success_cleanup(self):
        result = self.evidence.run("import sys; print('FAULT:io:wal'); sys.exit(17)", check=False)
        assert_fault(result, "io", "wal")
        self.evidence.run("print('verified')")
        self.evidence.finish()
        self.assertFalse(self.evidence.directory.exists())
        self.assertEqual([], list(pathlib.Path(self.artifacts.name).iterdir()))

    def test_marker_alone_does_not_validate_exit_status(self):
        for code in (0, 23):
            result = self.evidence.run(f"import sys; print('FAULT:io:wal'); sys.exit({code})", check=False)
            with self.assertRaises(RuntimeError):
                assert_fault(result, "io", "wal")

    def test_copy_failure_keeps_original_evidence(self):
        with patch("migration_recovery_evidence.shutil.copytree", side_effect=OSError("copy failed")):
            with self.assertRaisesRegex(OSError, "copy failed"):
                self.evidence.finish(RuntimeError("original"))
        self.assertTrue((self.evidence.directory / "evidence.json").is_file())
        for name, data in self.fixture.items():
            self.assertEqual(data, (self.evidence.directory / name).read_bytes())


if __name__ == "__main__":
    unittest.main()
