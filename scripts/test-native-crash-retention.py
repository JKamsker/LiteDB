#!/usr/bin/env python3
"""Exercise real killed rebuild children -> failure manifests -> post-host copies."""
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
parser.add_argument('--architecture', default={'amd64': 'x64', 'x86_64': 'x64', 'aarch64': 'arm64'}.get(platform.machine().lower(), platform.machine().lower()))
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=False)
root = args.output.resolve()
env = dict(os.environ, LITEDB_RETAINED_FIXTURES=str(root), LITEDB_NATIVE_CRASH_RETENTION_SENTINEL='1')
assembly = Path('LiteDB.Tests/bin/Release') / args.framework / 'LiteDB.Tests.dll'
major = int(args.framework.removeprefix('net').split('.')[0])
result = subprocess.run(['dotnet', 'vstest', str(assembly), f'/Framework:.NETCoreApp,Version=v{major}.0',
    f'/Platform:{args.architecture}', '/Settings:tests.runsettings',
    '/TestCaseFilter:FullyQualifiedName~NativeAdmissionCrash_Tests'],
    env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=300)
(root / 'expected-failing-test-host.log').write_text(result.stdout, encoding='utf-8')
assert result.returncode != 0, 'The deliberately failing native crash host unexpectedly passed or did not run'
manifests = list(root.glob('*.json'))
assert len(manifests) == 24, f'Expected 24 real crash manifests, got {len(manifests)}'
for manifest in manifests:
    data = json.loads(manifest.read_text(encoding='utf-8-sig'))
    assert data['fixtureKind'] == 'native-crash-directory'
    assert data['childExitObserved'] and data['childPid'] > 0
    assert 'native crash retention sentinel after child exit' in data['failure'], data['failure']
    assert not Path(data['directory']).is_relative_to(root), 'Exercised fixture moved to artifact storage'
spec = importlib.util.spec_from_file_location('collector', Path(__file__).with_name('collect-retained-fixtures.py'))
collector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(collector)
assert collector.collect(root) == 0, 'Post-host native fixture collection failed'
markers = 0
for manifest in manifests:
    data = json.loads(manifest.read_text(encoding='utf-8-sig'))
    directory = Path(data['directory'])
    report = json.loads((root / manifest.stem / 'collection-report.json').read_text())
    assert report['errors'] == []
    sources = list(directory.rglob('*'))
    assert len(list(directory.glob('*.db'))) >= 2, 'Missing original/replacement/backup database evidence'
    for original in sources:
        if original.is_file():
            copy = root / manifest.stem / original.relative_to(directory)
            assert hashlib.sha256(copy.read_bytes()).digest() == hashlib.sha256(original.read_bytes()).digest()
            if original.name == 'data-rebuild.db':
                markers += 1
assert markers == 20, f'Expected every post-marker crash boundary, got {markers}'
print('Passed: 24 real crash fixtures retained in place with byte-identical post-host copies, including 20 recovery markers')
