#!/usr/bin/env python3
"""Exercise the real C# graph failure -> manifest -> post-host fixture artifact path."""
import argparse
import hashlib
import importlib.util
import json
import os
import platform
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--framework', default='net8.0')
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--architecture', default={'amd64': 'x64', 'x86_64': 'x64', 'aarch64': 'arm64'}.get(platform.machine().lower(), platform.machine().lower()))
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
root = args.output.resolve()
env = dict(os.environ, LITEDB_RETAINED_FIXTURES=str(root), LITEDB_GRAPH_RETENTION_SENTINEL='1')
# Execute the downloaded assembly directly: packaged CI outputs intentionally do
# not include root obj/project.assets.json, so a no-build project test invocation
# can silently do no work when its imported IsTestProject property is unavailable.
assembly = Path('LiteDB.Tests/bin/Release') / args.framework / 'LiteDB.Tests.dll'
major = int(args.framework.removeprefix('net').split('.')[0])
result = subprocess.run(['dotnet', 'vstest', str(assembly),
                         f'/Framework:.NETCoreApp,Version=v{major}.0', f'/Platform:{args.architecture}',
                         '/Settings:tests.runsettings',
                         '/TestCaseFilter:FullyQualifiedName~NativeAdmissionGraphFinalizer_Tests'],
                        env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=90)
(root / 'expected-failing-test-host.log').write_text(result.stdout, encoding='utf-8')
assert result.returncode != 0, 'The deliberately failing graph test unexpectedly passed or did not execute:\n' + result.stdout
manifests = list(root.glob('*.json'))
assert len(manifests) == 6, f'Expected six actual C# graph manifests, got {len(manifests)}'
for manifest in manifests:
    data = json.loads(manifest.read_text(encoding='utf-8-sig'))
    assert 'graph retention sentinel after real graph construction' in data['failure'], data['failure']
    assert not Path(data['database']).is_relative_to(root), 'Exercised fixture was moved to artifact storage'
spec = importlib.util.spec_from_file_location('collector', Path(__file__).with_name('collect-retained-fixtures.py'))
collector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(collector)
assert collector.collect(root) == 0, 'Post-host collection failed'
coordination_copies = 0
for manifest in manifests:
    data = json.loads(manifest.read_text(encoding='utf-8-sig'))
    report = json.loads((root / manifest.stem / 'collection-report.json').read_text())
    database = Path(data['database'])
    for original in (database, database.with_name(database.stem + '-log' + database.suffix)):
        copied = root / manifest.stem / original.name
        assert copied.stat().st_size > 0, f'Missing exercised database/WAL: {copied}'
        assert hashlib.sha256(copied.read_bytes()).digest() == hashlib.sha256(original.read_bytes()).digest()
    # Match every source file beneath this GUID's coordination directories, not
    # only the data/WAL. Shared exhausted readers retain actual reader lease files.
    for original in database.parent.glob(database.stem + '*'):
        if original.is_dir():
            for source in original.rglob('*'):
                if source.is_file():
                    copied = root / manifest.stem / source.relative_to(database.parent)
                    assert copied.read_bytes() == source.read_bytes(), f'Coordination evidence missing: {source}'
                    coordination_copies += 1
    assert report['errors'] == []
assert coordination_copies >= 2, 'Shared graph fixtures did not exercise coordination retention'
print('Passed: six failing real engine graphs retained original assertions and byte-identical database/WAL artifacts after host exit')
