# Issue 3067: Handle begin waits on native ownership already held by its caller

Reproduces [LiteDB issue #3067](https://github.com/litedb-org/LiteDB/issues/3067).

## Expected outcome

Against the known-bad LiteDB `0.0.0-knownbad.560529066aed` pinned in the `.csproj` the repro exits `0` (the bug
reproduces). Against the fixed in-repo source it exits non-zero. The **Regression proof** workflow
requires both; see `docs/rules/safety-evidence.md#regression-proofs`.

```bash
dotnet run --project LiteDB.ReproRunner/LiteDB.ReproRunner.Cli -- run Issue_3067_SharedNestedBegin
```
