# Issue 3067: Surfaced pin core close failure skips Shared connection resource cleanup

Reproduces [LiteDB issue #3067](https://github.com/litedb-org/LiteDB/issues/3067).

## Expected outcome

Against the known-bad LiteDB `0.0.0-knownbad.eb01f346eb7d` pinned in the `.csproj` the repro exits `0` (the bug
reproduces). Against the fixed in-repo source it exits non-zero. The **Regression proof** workflow
requires both; see `docs/rules/safety-evidence.md#regression-proofs`.

```bash
dotnet run --project LiteDB.ReproRunner/LiteDB.ReproRunner.Cli -- run Issue_3067_SharedPinCleanup
```

This Linux-only black-box proof uses public database APIs and `RLIMIT_FSIZE` to
reject the header-recovery append during a real pinned core's close checkpoint.
It restores the file-size limit before verification. On .NET/Linux EFBIG can be
reported as `ArgumentOutOfRangeException` (`value`) rather than `IOException`.
It requires the expected close failure, checks the surviving snapshot, then
checks whether its last lease file remains held after reader and facade disposal.
The fixed case also cold-reopens twice and checks the committed indexed model.
This is an exception/I/O-failure model, not process death or power loss.

The source variant requires exit `10` plus the explicit verification marker;
unexpected setup errors exit `1` and cannot masquerade as a fixed result.
