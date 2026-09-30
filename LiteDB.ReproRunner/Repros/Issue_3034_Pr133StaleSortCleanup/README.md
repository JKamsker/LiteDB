# PR 133: stale sort sidecar cleanup

This reproduces finding 8 in the review of [PR #133](https://github.com/JKamsker/LiteDB/pull/133),
with evidence registered under [the safety process](https://github.com/litedb-org/LiteDB/issues/3034).

The repro seeds a file-backed indexed record and an unrelated collection, places a stale
`-tmp` sidecar beside the closed database, then opens and closes a writable database without
spilling. Committed reads and a cold reopen must remain correct; the stale sidecar must go.
The setup models an already-leftover scratch file; it does not model power loss or kill a process.

Against `0.0.0-knownbad.560529066aed` the repro exits 0 because the stale file survives.
Against fixed source it exits 10 because cleanup succeeds. The ReproRunner requires that pair.
The base commit `49c327cf1926fa300f9eb7477eb4bcb404f75c43` cleans up too.

The permanent guards additionally cover plain/encrypted Direct and Shared writers, native
admission refusal through aliases, and a read-only peer whose close must preserve another
reader's real spilled sort. Read-only opens deliberately do not claim unused shared scratch
paths; coordinated read snapshots already use their own private temp storage.

```bash
dotnet run --project LiteDB.ReproRunner/LiteDB.ReproRunner.Cli -- run Issue_3034_Pr133StaleSortCleanup
```
