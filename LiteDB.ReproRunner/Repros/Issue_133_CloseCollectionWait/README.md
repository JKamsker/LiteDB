# PR 133: Raw engine close waits for collection timeout

Reproduces [fork PR #133](https://github.com/JKamsker/LiteDB/pull/133).

## Expected outcome

Against the known-bad LiteDB `0.0.0-knownbad.560529066aed` pinned in the `.csproj` the repro exits `0` (the bug
reproduces). Against the fixed in-repo source it exits `10` and prints `VERIFIED_FIXED`. Unexpected failures exit `1` and fail the proof. The **Regression proof** workflow
requires both; see `docs/rules/safety-evidence.md#regression-proofs`.

```bash
dotnet run --project LiteDB.ReproRunner/LiteDB.ReproRunner.Cli -- run Issue_133_CloseCollectionWait
```
