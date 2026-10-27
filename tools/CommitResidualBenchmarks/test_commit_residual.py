import json
from pathlib import Path
import tempfile
import unittest

from analyze_commit import audit_eof, completed_calls
from instrument_commit import mask, wrap_methods
from commit_residual import summarize


class CommitAnalysisTests(unittest.TestCase):
    def test_wrapping_preserves_literals_comments_and_overloads(self):
        code = '''public void Commit() { var a = "}"; /* } */ if (true) { } }
public void Commit(int n) { // }
return; }
public int Other() => 1;'''
        changed, found = wrap_methods(code, ["Commit"], 1)
        self.assertEqual(found, ["Commit", "Commit"])
        self.assertEqual(changed.count("P1KProbe.Enter(1)"), 2)
        self.assertIn('var a = "}";', changed)
        self.assertEqual(mask(changed).count("{"), mask(changed).count("}"))

    def test_never_wraps_expression_body_or_iterator_local(self):
        code = '''private T AutoTransaction<T>(Func<T> f) => Execute(f);
private int PersistDirtyPages(bool commit) {
IEnumerable<int> source() { yield return 1; }
return source().Count();
}'''
        changed, found = wrap_methods(code, ["AutoTransaction", "PersistDirtyPages"], 1)
        self.assertEqual(found, ["PersistDirtyPages"])
        self.assertIn('source() { yield return 1; }', changed)

    def test_eof_audit_counts_real_extensions_and_short_writes(self):
        lines = [
            '1 1.0 pwrite64(4</tmp/a-log.db>, "a", 8256, 0) = 8256 <0.01>',
            '1 1.1 write(2, "P1D_BEGIN single_insert_5k_tx\\n", 39) = 39 <0.01>',
            '1 1.2 ftruncate(4</tmp/a-log.db>, 16384) = 0 <0.01>',
            '1 1.3 ftruncate(4</tmp/a-log.db>, 16384) = 0 <0.01>',
            '1 1.4 pwrite64(4</tmp/a-log.db>, "b", 8256, 8256) = 100 <0.01>',
            '1 1.5 ftruncate(4</tmp/a-log.db>, 8192) = 0 <0.01>',
            '1 1.6 ftruncate(4</tmp/a-log.db>, 1) = -1 EIO <0.01>',
            '1 1.7 write(2, "P1D_END single_insert_5k_tx\\n", 37) = 37 <0.01>',
            '1 1.8 ftruncate(4</tmp/a-log.db>, 0) = 0 <0.01>']
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "trace.txt"
            path.write_text("\n".join(lines))
            self.assertEqual(audit_eof(path)["transitions"], dict(extend=1, equal=1, shrink=1, unknown=0))

    def test_unfinished_call_keeps_its_start_window(self):
        lines = [
            '1 1.0 write(2, "P1D_BEGIN single_insert_5k_tx\\n", 39) = 39 <0.01>',
            '1 1.1 ftruncate(4</tmp/a-log.db>, 8192 <unfinished ...>',
            '2 1.2 write(2, "P1D_END single_insert_5k_tx\\n", 37) = 37 <0.01>',
            '1 1.3 <... ftruncate resumed>) = 0 <0.02>']
        selected, call = list(completed_calls(lines))[-1]
        self.assertEqual(selected, "single_insert_5k_tx")
        self.assertIn("ftruncate", call)

    def test_summary_rejects_missing_rounds(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp)
            (path / "round-1-x.json").write_text(json.dumps(dict(config="x", samples=[])))
            with self.assertRaisesRegex(ValueError, "Incomplete"):
                summarize(path, 6)

    def test_summary_normalizes_components_and_rejects_device_barrier(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp)
            for number in range(1, 7):
                result = dict(config="phase1-no-checkpoint", assemblySha256="fixed", samples=[dict(
                    milliseconds=500, cpuMs=250, allocatedBytes=10000,
                    components=dict(totalMs=500, rows=[
                        dict(component="transaction-begin-commit", milliseconds=400, calls=10000),
                        dict(component="DEVICE-SYNC", milliseconds=100, calls=0)]))])
                (path / f"component-{number}-phase1-no-checkpoint.json").write_text(json.dumps(result))
            summarize(path, 6)
            row = json.loads((path / "commit-summary.json").read_text())[0]
            self.assertEqual(row["msPerCommit"], .1)
            self.assertEqual(row["components"][0]["msPerCommit"], .08)
            self.assertEqual(row["components"][0]["callsPerCommit"], 2)
            result["samples"][0]["components"]["rows"][1]["calls"] = 1
            (path / "component-6-phase1-no-checkpoint.json").write_text(json.dumps(result))
            with self.assertRaisesRegex(ValueError, "Unexpected cache-only barrier"):
                summarize(path, 6)


if __name__ == "__main__":
    unittest.main()
