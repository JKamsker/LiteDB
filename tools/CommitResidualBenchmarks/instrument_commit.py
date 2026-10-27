"""Create disposable CI-only diagnostic sources. Never edit the measured production checkout."""
import json
from pathlib import Path
import re
import shutil

HERE = Path(__file__).resolve().parent
# Category ids match diagnostic-probe.cs.txt. Nested scopes charge only the active
# innermost category; iterator bodies are deliberately NOT wrapped across yield.
RULES = [
    ("Engine/Engine/Transaction.cs", ["ExecuteAutoTransaction", "AutoTransaction", "CommitAndReleaseTransaction"], 1),
    ("Engine/Services/TransactionService.cs", ["TransactionService", "CreateSnapshot", "Commit", "PersistDirtyPages", "PersistAndConfirm", "ReleaseCommitSnapshots", "Dispose"], 1),
    ("Engine/Services/TransactionMonitor.cs", ["GetTransaction", "ReleaseTransaction", "RemoveTransaction", "CheckSafepoint", "GetThreadTransaction"], 2),
    ("Engine/Services/TransactionService.cs", ["Safepoint"], 2),
    ("Engine/Services/TransactionRegistry.cs", ["Add", "Remove"], 2),
    ("Engine/Services/LockService.cs", ["EnterTransaction", "ExitTransaction", "EnterLock", "ExitLock"], 2),
    ("Engine/Services/TransactionGate.cs", ["TryEnterReadLock", "ExitReadLock"], 2),
    ("Engine/Disk/Streams/ChecksummedWalStream.cs", ["Write", "WriteFinalPadded"], 3),
    ("Engine/Disk/WalChecksum.cs", ["Prepare", "Accept"], 3),
    ("Engine/Disk/WalPadding.cs", ["Pad"], 4),
    ("Engine/Disk/Streams/ChecksummedWalStream.cs", ["Flush"], 5),
    ("Engine/Disk/DiskService.DurableFlush.cs", ["FlushConfirmedLog"], 5),
    ("Engine/Services/WalIndexService.cs", ["ConfirmTransaction", "GetPageIndex", "NextTransactionID"], 6),
    ("Engine/Services/WalIndexService.Checkpoint.cs", ["PinSnapshot", "UnpinSnapshot"], 6),
    ("Engine/Disk/MemoryCache*.cs", ["GetReadablePage", "GetWritablePage", "NewPage", "MoveToReadable", "TryMoveToReadable", "DiscardPage", "Release", "TrimToLimit"], 6),
    ("Engine/Services/TransactionMonitor.cs", ["ThrowIfDisposed"], 7),
    ("Engine/EngineState.cs", ["Validate", "ThrowIfStopped"], 7),
    ("Engine/Services/Snapshot.StorageAdmission.cs", ["ShouldPrepareCompact"], 8),
    ("Engine/Storage/DocumentStorageCodec.cs", ["PrepareWrite"], 8),
    ("Engine/Storage/CompactDocumentWriter.cs", ["CompactDocumentWriter", "Encode", "Dispose"], 8),
    ("Engine/Disk/DiskService.WalWrite.cs", ["WriteLogDisk", "WriteLogPage"], 10),
    ("Engine/Disk/DiskService.cs", ["WriteLogDisk"], 10),
    ("Engine/Disk/WalFileAllocation.cs", ["Reserve"], 12),
    ("Engine/Services/WalCommitGroup.cs", ["WalCommitGroup", "TryCommit", "Append", "EnsureDurable", "CaptureDependency"], 15),
    ("Utils/NativeFileSync.cs", ["FlushToDisk", "FlushDevice", "Sync", "SyncDirectory"], 13),
    ("Engine/Services/WalIndexService.Checkpoint.cs", ["TryCheckpointCore", "CheckpointInternal"], 14),
    ("Engine/Services/WalIndexService.cs", ["CheckpointInternal"], 14),
]


def mask(code):
    # Keep offsets/newlines, hide comments and C# literals before balancing braces.
    token = r'//[^\n]*|/\*[\s\S]*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\''
    return re.sub(token, lambda m: re.sub(r"[^\r\n]", " ", m[0]), code)


def close_brace(masked, opening):
    depth = 0
    for i in range(opening, len(masked)):
        if masked[i] == "{": depth += 1
        elif masked[i] == "}":
            depth -= 1
            if depth == 0: return i
    raise ValueError("Unbalanced source")


def wrap_methods(code, names, category):
    masked = mask(code)
    edits, hits = [], []
    for name in names:
        pattern = (r"^[ \t]*(?:(?:public|private|internal|protected|static|override|virtual|sealed)\s+)+"
                   r"[^;{}=]*?\b" + re.escape(name) + r"(?:<[^>]+>)?\s*\([^;{}]*?\)\s*\{")
        for match in re.finditer(pattern, masked, re.M):
            opening = match.end() - 1
            closing = close_brace(masked, opening)
            edits += [(opening + 1, f"\nusing (global::LiteDB.P1KProbe.Enter({category})) {{\n"),
                      (closing, "\n}\n")]
            hits.append(name)
    for offset, value in sorted(edits, reverse=True):
        code = code[:offset] + value + code[offset:]
    return code, hits


def instrument(root, manifest):
    root = Path(root)
    if root.resolve() == HERE.parents[1]:
        raise ValueError("Refusing to instrument the production checkout")
    hits = []
    for glob, methods, category in RULES:
        for path in sorted((root / "LiteDB").glob(glob)):
            code, found = wrap_methods(path.read_text(encoding="utf-8-sig"), methods, category)
            path.write_text(code, encoding="utf-8")
            hits += [dict(file=str(path.relative_to(root)), method=m, category=category) for m in found]
    # Getter has an explicit body; record negative eligibility checks without
    # enabling/disabling group commit or changing short-circuit evaluation.
    group = root / "LiteDB/Engine/Disk/DiskService.CommitGroup.cs"
    if group.exists():
        code = group.read_text()
        masked = mask(code)
        match = re.search(r"internal WalCommitGroup CommitGroup\s*\{\s*get\s*\{", masked)
        if not match: raise ValueError("Group getter boundary changed")
        opening = match.end() - 1
        closing = close_brace(masked, opening)
        code = code[:closing] + "\n}\n" + code[closing:]
        code = code[:opening+1] + "\nusing (global::LiteDB.P1KProbe.Enter(9)) {\n" + code[opening+1:]
        group.write_text(code)
        hits.append(dict(file=str(group.relative_to(root)), method="get_CommitGroup", category=9))
    # Distinguish physical frame writes from checksum/copy and WAL bookkeeping.
    for rel in ["Engine/Disk/Streams/ChecksummedWalStream.cs", "Engine/Disk/DiskService.cs"]:
        path = root / "LiteDB" / rel
        if not path.exists(): continue
        code = path.read_text(encoding="utf-8-sig")
        statements = (["_stream.Write(_frame, 0, _frame.Length);", "_stream.Write(combined, 0, count);"] if "ChecksummedWal" in rel else
                      ["stream.Write(page.Array, page.Offset, PAGE_SIZE);"])
        for statement in statements:
            if statement in code:
                code = code.replace(statement, "using (global::LiteDB.P1KProbe.Enter(11)) { " + statement + " }")
                hits.append(dict(file=str(path.relative_to(root)), method=statement, category=11))
        path.write_text(code, encoding="utf-8")
    for method in ["GetTransaction", "ReleaseTransaction", "Safepoint", "Commit", "WriteLogDisk", "ConfirmTransaction"]:
        if not any(h["method"] == method for h in hits):
            raise ValueError("Missing diagnostic boundary: " + method)
    shutil.copyfile(HERE / "diagnostic-probe.cs.txt", root / "LiteDB/P1KProbe.cs")
    Path(manifest).write_text(json.dumps(hits, indent=2))
    return hits
