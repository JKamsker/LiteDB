# Spatial Benchmarks

LiteDB ships with a dedicated BenchmarkDotNet suite that exercises the spatial facade across the most common workloads. Use these runs to validate branch health, capture regressions, and compare engines after tuning precision or distance tolerances.

## Prerequisites

- Restore and build the solution first: `dotnet restore` followed by `dotnet build LiteDB.sln -c Release`.
- Ensure no other LiteDB processes are using the benchmark database path. The harness creates temporary files per run.

## Running the suite

Execute the benchmarks from the repository root:

```powershell
dotnet run --project LiteDB.Benchmarks/LiteDB.Benchmarks.csproj -- --spatial-only
```

The trailing `--spatial-only` flag maps to the `SPATIAL` BenchmarkDotNet category and limits the run to `SpatialQueryBenchmarks`. Omit the flag to execute the full benchmark catalog.

BenchmarkDotNet arguments continue to work alongside the spatial filter. For example, to run only the `NearQuery` scenario:

```powershell
dotnet run --project LiteDB.Benchmarks/LiteDB.Benchmarks.csproj -- --spatial-only --filter *NearQuery*
```

When the run completes, reports are written to `BenchmarkDotNet.Artifacts`, including GitHub-flavoured markdown that you can attach to PR discussions.

## Result interpretation checklist

- Validate that `SpatialApi.UseGeographic` completes during global setup—failures usually indicate missing descriptor metadata or mapper registration.
- Compare the `NearQuery` and `BoundingBoxQuery` allocations to confirm that anti-meridian route planning remains on the fast path (range scans followed by bounding box filtering).
- Track the `Mean` column for each dataset size when adjusting precision bits; large regressions often signal that Morton keys are saturating and forcing broader scans.

Refer to `docs/spatial-guide.md` for configuration background and end-to-end examples that mirror the benchmark setup.

## Validation suites

Complement the BenchmarkDotNet runs with the deterministic test harnesses that ship alongside the spatial core.

### Locality fixtures

The `Indexing/LocalityTests` suite regenerates Morton codes for recorded fixtures and asserts top-k neighbour overlap. Run it whenever you touch the encoder or mapper implementations:

```bash
dotnet test LiteDB.Spatial.Core.Tests --filter FullyQualifiedName~LocalityTests
```

The fixtures live under `LiteDB.Spatial.Core.Tests/Fixtures` and are copied to the test output automatically.

### LINQ plan parity

`Linq/SpatialExpressionsDiagnosticsTests` exercises representative `SpatialExpressions` queries, runs them through `SpatialDiagnostics.Explain`, and fails if the explain output degrades to full scans or hides `_idx`/`_mbb` prefilters:

```bash
dotnet test LiteDB.Spatial.Core.Tests --filter FullyQualifiedName~SpatialExpressionsDiagnosticsTests
```

### Perf harness (`perf` category)

Set `LITEDB_SPATIAL_PERF_CAPTURE=1` to enable the optional perf harness. The harness reuses the locality fixtures to compare LiteDB range scans against SQLite RTree and PostGIS. Provide connection strings via environment variables before invoking the tests:

```bash
export LITEDB_SPATIAL_PERF_CAPTURE=1
export LITEDB_SPATIAL_PERF_SQLITE="Data Source=spatial-perf.sqlite"
export LITEDB_SPATIAL_PERF_POSTGRES="Host=localhost;Username=postgres;Password=postgres;Database=spatial"
dotnet test LiteDB.Spatial.Core.Tests --filter Category=perf
```

The harness writes aggregate results to `docs/benchmarks.md`. Commit the updated markdown alongside code changes when capturing new snapshots.

#### External database notes

- **SQLite RTree**: the bundled `Microsoft.Data.Sqlite` provider ships with RTree support enabled. Point `LITEDB_SPATIAL_PERF_SQLITE` at a writable file path or `Data Source=:memory:` for ephemeral runs.
- **PostGIS**: spin up an instance with Docker, for example `docker run --rm -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgis/postgis`. Create the `spatial` database and supply its connection string via `LITEDB_SPATIAL_PERF_POSTGRES`.

After the harness executes, review `docs/benchmarks.md` for the captured candidate counts and durations. Re-run the suite whenever tuning index precision or query planners.
