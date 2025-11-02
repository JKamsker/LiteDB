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
