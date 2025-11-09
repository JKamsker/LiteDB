# Quickstart - Vector Stability Hardening

Follow these steps to keep verification repeatable while working through the vector stability phases.

## Environment Prep
1. Restore tooling once per session: `dotnet restore LiteDB.sln`
2. Ensure the feature branch (`001-vector-stability`) is checked out and clean: `git status --short`

## US1 Fail-First Instructions
Run this loop before implementing the dot-product fix to prove the regression exists:
1. Build the tests: `dotnet build LiteDB.Vector.Tests/LiteDB.Vector.Tests.csproj -c Debug`
2. Execute only the new regression test (naming convention: `DotProductMaxDistanceRegression` inside `VectorIndex_Tests`):
   ```
   dotnet test LiteDB.Vector.Tests/LiteDB.Vector.Tests.csproj `
     -f net8.0 `
     --filter FullyQualifiedName~DotProductMaxDistanceRegression
   ```
3. **Expected outcome before the fix**: the test fails because metadata-provided `maxDistance` is not normalized. Record the failure message in this file when first observed.
4. Do not move on to production fixes until this failure is reproduced.

## End-to-End Verification Steps
Run the following sequence whenever you need to validate accumulated changes:

| Order | Command | Purpose |
| --- | --- | --- |
| 1 | `dotnet test LiteDB.Vector.Tests/LiteDB.Vector.Tests.csproj -f net8.0` | Covers the dedicated vector plugin suite (net8.0 target). |
| 2 | `dotnet test LiteDB.Tests/LiteDB.Tests.csproj -f net8.0 --filter FullyQualifiedName~Vector` | Ensures shared engine/query code passes focused vector scenarios. |
| 3 | `pwsh -File scripts/run-tests-per-target.ps1 -Projects LiteDB.Tests,LiteDB.Vector.Tests` | Exercises every configured target framework (includes the net462 xUnit fallback). |
| 4 | `dotnet test LiteDB.sln --settings tests.runsettings` | Full solution verification with the repo-wide timeout profile. |
| 5 | `git status --short` | Confirms no generated artifacts or doc edits are missing from commits. |
| 6 | `git diff --stat` | Provides a quick summary to attach to progress updates/PR descriptions. |

## Troubleshooting Notes
- If the fail-first regression unexpectedly passes before the fix, double-check that `LiteDB.Vector.Tests/bin` is cleared (`dotnet clean LiteDB.Vector.Tests`) and rerun step 2 above to ensure cached binaries are not masking the issue.
- When `scripts/run-tests-per-target.ps1` executes the `net462` target, the script automatically invokes `xunit.console`. Do not run vstest manually because it lacks the `Microsoft.Bcl.AsyncInterfaces` dependency.
- For artifact cleanup validation (US4), generate outputs via `scripts/vector/Invoke-VectorUpgrade.ps1`, then re-run `git status --short` to confirm `artifacts_temp/` stays clean.
