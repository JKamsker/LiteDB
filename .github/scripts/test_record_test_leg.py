import json
import os
import re
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import record_test_leg as record
from safety_fixtures import GitRepo


class FilteredLegEvidenceTests(unittest.TestCase):
    def test_every_workflow_job_is_declared_and_waited_for(self):
        root = Path(__file__).resolve().parents[2]
        workflow = (root / '.github/workflows/_reusable-ci.yml').read_text()
        jobs = set(re.findall(r'^  ([\w-]+):$', workflow.split('\njobs:\n', 1)[1], re.MULTILINE))
        jobs.remove('safety-evidence')
        config = json.loads((root / '.github/safety/ci-evidence.json').read_text())
        self.assertEqual(jobs, set(config['jobs']))
        aggregate = workflow.split('  safety-evidence:', 1)[1]
        needs = re.search(r'needs: \[([^\]]+)\]', aggregate).group(1)
        self.assertEqual(jobs, {job.strip() for job in needs.split(',')})
        for job in ('test-native-glibc231', 'test-native-macos-intel', 'test-native-windows-arm64'):
            self.assertEqual(config['jobs'][job]['tiers'], ['full'])
            self.assertIn('full', config['jobs'][job]['legs'])

    def test_filter_matches_union_intersection_and_exclusion(self):
        expression = "FullyQualifiedName~Native&FullyQualifiedName!~Process|FullyQualifiedName~Handle"
        self.assertTrue(record.selected("Tests.Native.Open", expression))
        self.assertFalse(record.selected("Tests.Native.Process", expression))
        self.assertTrue(record.selected("Tests.Handle.Process", expression))
        self.assertFalse(record.selected("Tests.Other", expression))
        with self.assertRaisesRegex(ValueError, "Unsupported"):
            record.selected("Tests.Other", "Priority=1")

    def test_records_selected_discovery_guards_and_binary_provenance(self):
        with GitRepo() as repo, tempfile.TemporaryDirectory() as temporary:
            repo.commit({"a": "candidate"})
            root = Path.cwd()
            build = root / "LiteDB.Tests/bin/Release/build-evidence.json"
            build.parent.mkdir(parents=True)
            build.write_text(json.dumps({"sha": "older-binary-sha"}))
            results = Path(temporary)
            listing = results / "listing.txt"
            listing.write_text("Tests.Native.Open\nTests.Other\nTests.TestHost_Tests.Guard\n")
            with patch.dict(os.environ, {"GITHUB_JOB": "test-native", "LITEDB_EVIDENCE_MATRIX": '{"framework":"net8.0"}'}):
                record.record(root, results, listing, "FullyQualifiedName~Native", "Native.trx", "net8.0", 8, "arm64")
            leg = json.loads((results / "evidence-leg.json").read_text())
            self.assertEqual(leg["buildSha"], "older-binary-sha")
            self.assertNotEqual(leg["sha"], leg["buildSha"])
            self.assertEqual(leg["job"], "test-native")
            self.assertEqual(leg["matrix"], {"framework": "net8.0"})
            self.assertEqual(leg["partitions"], {"native": "Native.trx"})
            self.assertEqual((results / "discovered-tests.txt").read_text().splitlines(),
                             ["Tests.Native.Open", "Tests.TestHost_Tests.Guard"])

    def test_missing_provenance_or_empty_discovery_cannot_claim_candidate_binaries(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            listing = root / "listing.txt"
            listing.write_text("Tests.Other\n")
            with self.assertRaisesRegex(ValueError, "no selected tests"):
                record.record(root, root, listing, "FullyQualifiedName~Native", "Native.trx", "net8.0", 8, "x64")
            with self.assertRaises(FileNotFoundError):
                record.record(root, root, listing, "", "Native.trx", "net8.0", 8, "x64")


if __name__ == "__main__":
    unittest.main()
