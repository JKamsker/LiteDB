# Spatial benchmark snapshots

This page tracks spatial regression tests and optional performance harnesses. All commands assume the repository root as the working directory.

## Locality regression tests

The `Indexing/LocalityTests` suite reproduces the grid fixtures stored in `LiteDB.Spatial.Core.Tests/Indexing/Fixtures/locality-fixtures.json` and verifies that Morton codes keep neighbourhood overlap above the recorded thresholds.

```bash
dotnet test LiteDB.Spatial.Core.Tests/LiteDB.Spatial.Core.Tests.csproj \
  --filter FullyQualifiedName~LocalityTests
```

Fixtures should only be regenerated with the helper located in `LiteDB.Spatial.Core.Tests/Indexing/Fixtures/` to avoid drift.

## LINQ parity tests

`SpatialExpressionsQueryTests` exercises representative `SpatialExpressions.Near` and `SpatialExpressions.InBox` predicates. The tests hydrate a temporary database, inspect the explain plan, and confirm that `_idx` index predicates and `_mbb` prefilters are applied before the exact distance filters.

```bash
dotnet test LiteDB.Spatial.Core.Tests/LiteDB.Spatial.Core.Tests.csproj \
  --filter FullyQualifiedName~SpatialExpressionsQueryTests
```

The assertions fail if the resolver falls back to full scans or if the explain output omits the expected predicate order.

## Perf harness

`SpatialPerformanceHarness` is opt-in and reuses the locality fixtures to compare LiteDB against external engines. It is disabled by default.

### Running the LiteDB baseline

```bash
LITEDB_SPATIAL_PERF=true \
LITEDB_SPATIAL_PERF_WRITE=true \
dotnet test LiteDB.Spatial.Core.Tests/LiteDB.Spatial.Core.Tests.csproj \
  --filter FullyQualifiedName~SpatialPerformanceHarness
```

* `LITEDB_SPATIAL_PERF` enables the harness.
* `LITEDB_SPATIAL_PERF_WRITE=true` persists the Markdown summary to `docs/benchmarks.md`. Override the path with `LITEDB_SPATIAL_PERF_OUTPUT` when needed.
* Provide `LITEDB_SPATIAL_PERF_EXTERNAL=/path/to/external.json` to append metrics gathered from other engines. The file should contain a payload matching:

  ```json
  {
    "scenarios": [
      {
        "Scenario": "Locality-2D",
        "Engine": "SQLite RTree",
        "CandidateCount": 42,
        "ResultCount": 12,
        "TotalCount": 64,
        "Duration": "00:00:00.015",
        "Notes": "sqlite3 rtree"
      }
    ]
  }
  ```

### Capturing external baselines

SQLite and PostGIS benchmarks are easiest with Docker:

```bash
# SQLite RTree shell
docker run --rm -it -v $(pwd)/perf:/perf keinos/sqlite3 sqlite3

# PostGIS 15 container listening on localhost:5432
docker run --rm -d --name postgis -e POSTGRES_PASSWORD=pass -p 5432:5432 postgis/postgis:15-3.4
```

Load the locality fixtures into each engine, execute the equivalent bounding-box or radius queries, and record candidate counts and timings in the JSON format above. Point `LITEDB_SPATIAL_PERF_EXTERNAL` at the snapshot before re-running the harness to merge the results into this page.

## Updating the snapshot

After running the harness with `LITEDB_SPATIAL_PERF_WRITE=true`, stage `docs/benchmarks.md` alongside any updated fixtures. Include the exact command invocation in commit messages or PR descriptions so the environment is reproducible.

_Last updated: 2025-10-09_

| Scenario | Engine | Candidates | Reduction | Duration (ms) | Results | Notes |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| Locality-2D | LiteDB | 56 | 0.875 | 98.21 | 12 | Cartesian2D Near |
| Locality-3D | LiteDB | 62 | 0.969 | 29.87 | 8 | Cartesian3D Near |

> Benchmarks are best-effort and should be re-run on the target hardware before publishing numbers externally.
