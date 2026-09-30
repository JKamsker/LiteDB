# Issue 133: Raw engine close waits for collection timeout

Reproduces [LiteDB issue #133](https://github.com/litedb-org/LiteDB/issues/133).

## Expected outcome

Against the known-bad LiteDB `0.0.0-knownbad.560529066aed` pinned in the `.csproj` the repro exits `0` (the bug
reproduces). Against the fixed in-repo source it exits non-zero. The **Regression proof** workflow
requires both; see `docs/rules/safety-evidence.md#regression-proofs`.

```bash
dotnet run --project LiteDB.ReproRunner/LiteDB.ReproRunner.Cli -- run Issue_133_CloseCollectionWait
```
