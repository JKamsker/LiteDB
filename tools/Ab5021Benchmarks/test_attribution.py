"""Synthetic parser checks only. No LiteDB workload, benchmark or profiler runs."""
import json
from pathlib import Path
import tempfile
import unittest

from analyze_attribution import perf_stacks, speedscope, syscall_sections
from attribution import DEV, WRITE_BASE, WRITE_PROTO, variant


class AttributionTests(unittest.TestCase):
    def test_configuration_selects_exact_reference(self):
        self.assertEqual(variant("v5-no-checkpoint"), ("v5", "V5", "nuget:5.0.21"))
        self.assertEqual(variant("dev-legacy-pages-cache"), ("dev", "V6", DEV))
        self.assertEqual(variant("write-base-fast"), ("write-base", "V6", WRITE_BASE))
        self.assertEqual(variant("write-proto-fast"), ("write-proto", "V6", WRITE_PROTO))

    def test_evented_profile_excludes_setup_and_partitions_inclusive_overlap(self):
        data = dict(shared=dict(frames=[dict(name=name) for name in [
            "setup", "Ab5021Benchmarks.Measurement.Scan()", "LiteDB.QueryExecutor.RunQuery()",
            "LiteDB.DocumentStorageCodec.Read()"]]), profiles=[dict(type="evented", unit="milliseconds",
            startValue=0, endValue=14, events=[dict(type=t, at=at, frame=f) for t, at, f in [
                ("O", 0, 0), ("C", 4, 0), ("O", 4, 1), ("O", 4, 2),
                ("O", 6, 3), ("C", 10, 3), ("C", 14, 2), ("C", 14, 1)]])])
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "trace.json"
            path.write_text(json.dumps(data))
            result = speedscope(path)["scenarios"]["full_scan_3x100k"]
        self.assertEqual(result["totalWeight"], 10)
        self.assertEqual({r["category"]: r["percent"] for r in result["partition"]},
                         {"query executor": 60, "compact codec": 40})
        self.assertEqual(sum(r["percent"] for r in result["partition"]), 100)

    def test_syscall_scope_excludes_setup_and_rejoins_split_calls(self):
        trace = '\n'.join([
            '7 1.000 fsync(3</tmp/main.db>) = 0 <0.3>',
            '7 2.000 write(2, "P1D_BEGIN single_insert_5k_tx\\n", 37) = 37 <0.1>',
            '7 2.100 ftruncate(3</tmp/small-log.db>, 8192) = 0 <0.002>',
            '8 2.200 fsync(3</tmp/small-log.db> <unfinished ...>',
            '8 2.250 <... fsync resumed>) = 0 <0.050>',
            '7 2.500 write(2, "P1D_END single_insert_5k_tx\\n", 35) = 35 <0.1>',
            '7 3.000 fsync(3</tmp/main.db>) = 0 <0.3>'])
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "trace.txt"
            path.write_text(trace)
            result = syscall_sections(path)["single_insert_5k_tx"]
        self.assertEqual(set(result), {"ftruncate:wal", "fsync:wal"})
        self.assertEqual(result["fsync:wal"], dict(calls=1, errors=0, summedSeconds=0.05))

    def test_kernel_profile_requires_measured_wrapper(self):
        trace = "app 1 1.0: cpu-clock:\n\t10 Setup (app)\n\napp 1 2.0: cpu-clock:\n\t20 LiteDB.WalPadding.Pad (map)\n\t30 Ab5021Benchmarks.Measurement.Single (map)\n\n"
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "perf.stacks"
            path.write_text(trace)
            result = perf_stacks(path)["scenarios"]
        self.assertEqual(list(result), ["single_insert_5k_tx"])
        self.assertEqual(result["single_insert_5k_tx"]["partition"][0]["category"], "WAL/checksums/padding")


if __name__ == "__main__":
    unittest.main()
