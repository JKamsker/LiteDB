# Baselines – Spatial Plugin Migration

## 2025-11-01 – Pre-migration Reference

- `dotnet build LiteDB.Benchmarks/LiteDB.Benchmarks.csproj -c Release`
  - Output size: `LiteDB.dll` 622.50 KB, `LiteDB.Spatial.dll` 29.00 KB
  - Warnings: nullable annotations (CS8632), crypto deprecation (SYSLIB0041), rethrow (CA2200), legacy query API (CS0618)
- `dotnet build LiteDB.Stress/LiteDB.Stress.csproj -c Release`
  - Output size: `LiteDB.dll` 622.50 KB

These measurements act as the baseline for post-migration performance and package size comparisons.

## 2025-11-01 – Post-core spatial removal

- `dotnet build LiteDB.sln -c Release`
  - Output size: `LiteDB.dll` 591.00 KB (net8.0 target)
  - Warnings: third-party net461 support (System.Text.Json stack), nullable annotations, analyzer suggestions (existing)
  - Notes: Spatial assemblies no longer produced from core build

## 2025-11-02 – Post-plugin spatial validation

### SpatialQueryBenchmarks (short-run)

- Command: `dotnet run --project LiteDB.Benchmarks/LiteDB.Benchmarks.csproj -c Release -- --job short --spatial-only --filter *SpatialQuery*`
- Artifact: `BenchmarkDotNet.Artifacts/results/LiteDB.Benchmarks.Benchmarks.Spatial.SpatialQueryBenchmarks-report-github.md`
- Highlights (direct connection, dataset size 500):
  - No password: `NearQuery` mean **31.66 μs**, `BoundingBoxQuery` mean **32.43 μs**, allocations **≈60 KB** each.
  - Encrypted: `NearQuery` mean **31.46 μs**, `BoundingBoxQuery` mean **32.31 μs**, allocations unchanged.
- Result: Spatial LINQ/interceptor stack holds parity with pre-migration short-run numbers while keeping allocation profile flat.

### Stress harness

- `dotnet run --project LiteDB.Stress/LiteDB.Stress.csproj -c Release -- --no-wait artifacts_temp/stress-test-01.xml 60s`
  - Log: `artifacts_temp/test-01.log`
  - Summary: **3006** `INSERT_COL1` executions (≈49.5 s runtime aggregate), **5** `UPDATE`/**DELETE** cycles with ≈0.83 M ops captured, steady memory envelope ≤165 MB.
- `dotnet run --project LiteDB.Stress/LiteDB.Stress.csproj -c Release -- --no-wait artifacts_temp/stress-test-02.xml 10s`
  - Log: `artifacts_temp/test-02.log`
  - Summary: Five insert pipelines executed **~200 ops each** in 10 s short-run, exercising concurrent writers without plugin regressions (shorter window prevents runaway WAL growth in the default sample).

## 2025-11-08 – Verification test evidence

- `dotnet test LiteDB.Tests/LiteDB.Tests.csproj -c Release --settings tests.runsettings`
  - Frameworks executed: `net8.0` (232 passed, 5 skipped, duration 3 s); `net461`/`net481` shims reported "no tests found" as expected.
  - Warnings: third-party net461 support (System.Text.* stack), nullable annotation scope (CS8632), legacy crypto warnings (SYSLIB0041).
- `dotnet test LiteDB.Spatial.Core.Tests/LiteDB.Spatial.Core.Tests.csproj -c Release --settings tests.runsettings`
  - Result: `net8.0` target – 111 passed, 0 skipped, duration 1 s.
  - Warnings: nullable annotation scope (CS8632) across `LiteDB.Spatial` runtime and test fixtures; XML doc param mismatch (CS1572/CS1573) for `SpatialQueryableExtensions`.
- Follow-up: Re-ran both commands after nullability clean-up; spatial plugin/runtime suites now emit zero nullable/doc-comment warnings (tests still pass: 232/0/5 skips for core, 111/0/0 for spatial).
