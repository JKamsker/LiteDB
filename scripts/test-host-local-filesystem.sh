#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
fixture=$(mktemp -d)
results="$repo_root/LiteDB.Tests/TestResults/host-local-f2fs"
mkdir -p "$results" "$fixture/primary" "$fixture/alias" "$fixture/single"
cleanup() {
    local status=$?
    if (( status != 0 )); then
        sudo tar -czf "$results/failed-volume-files.tar.gz" -C "$fixture/primary" . || true
    fi
    sudo umount "$fixture/single/data.db" 2>/dev/null || true
    sudo umount "$fixture/alias" 2>/dev/null || true
    sudo umount "$fixture/primary" 2>/dev/null || true
    rm -rf "$fixture"
    return "$status"
}
trap cleanup EXIT
sudo modprobe f2fs
truncate -s 512M "$fixture/volume.img"
mkfs.f2fs -f "$fixture/volume.img" > "$results/mkfs.log"
sudo mount -o loop "$fixture/volume.img" "$fixture/primary"
sudo chown "$(id -u):$(id -g)" "$fixture/primary"
sudo mount --bind "$fixture/primary" "$fixture/alias"
findmnt "$fixture/primary" > "$results/mount.txt"
export LITEDB_VALIDATE_HOST_LOCAL=1
runner="$repo_root/tools/NativeAdmissionValidation/bin/Release/net8.0/NativeAdmissionValidation.dll"
dotnet "$runner" fallback-default "$fixture/primary/data.db" | tee "$results/default-refusal.log"
dotnet "$runner" scenario "$fixture/primary" "$fixture/alias" | tee "$results/production-scenario.log"
python3 - "$runner" "$fixture" <<'PY'
import hashlib
from pathlib import Path
import subprocess
import sys
runner, root = sys.argv[1], Path(sys.argv[2])
data = root / 'primary/data.db'
wal = root / 'primary/data-log.db'
child = subprocess.Popen(['dotnet', runner, 'hold-wal', str(data)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True)
try:
    assert child.stdout.readline().strip() == 'wal-ready'
    assert wal.stat().st_size > 0
    before = [hashlib.sha256(p.read_bytes()).digest() for p in (data, wal)]
    alias = root / 'single/data.db'
    alias.touch()
    subprocess.run(['sudo', 'mount', '--bind', str(data), str(alias)], check=True)
    subprocess.run(['dotnet', runner, 'file-mount', str(alias)], check=True)
    assert before == [hashlib.sha256(p.read_bytes()).digest() for p in (data, wal)]
finally:
    child.kill()
    child.wait(timeout=20)
for _ in range(2):
    subprocess.run(['dotnet', runner, 'verify', str(data)], check=True)
PY
