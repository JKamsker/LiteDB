# Spatial Benchmark and Test Guide

This document captures the reproducible test suites and performance harnesses used to validate the spatial indexing stack. The sections below explain how to run locality and LINQ parity tests, configure optional database dependencies, and refresh the benchmark snapshots that live in this file.

## Locality Metrics Suite

The locality suite validates that synthetic grids preserve nearest-neighbour overlap after Morton encoding. It compares the runtime encoder output against fixtures under `LiteDB.Spatial.Core.Tests/Indexing/Fixtures` and enforces average/worst overlap thresholds.

* Run all locality metrics: `dotnet test LiteDB.Spatial.Core.Tests --filter LocalityTests`
* Add new fixture data to `morton-locality-fixtures.json` and regenerate metrics by re-running the command above.

## LINQ Explain Parity Suite

The LINQ parity suite issues representative `SpatialExpressions` queries against in-memory LiteDB collections, inspects `SpatialDiagnostics.Explain`, and fails when the engine falls back to full scans or omits `_idx`/`_mbb` prefilters.

* Execute the tests: `dotnet test LiteDB.Spatial.Core.Tests --filter SpatialExpressionsExplainTests`
* When adding new engines, use the helpers in `TestSupport/SpatialExplainAssertions.cs` to assert explain ordering and index participation.

## Optional Performance Harness

Set `LITEDB_SPATIAL_PERF=1` to opt into the performance harness. The harness reuses the locality fixtures to populate LiteDB, SQLite RTree, and PostGIS tables, records candidate reduction metrics, and rewrites the summary section of this file.

### Dependencies and Docker Recipes

* **LiteDB**: No extra configuration required—the harness provisions in-memory collections.
* **SQLite RTree**: Provide a connection string via `LITEDB_SPATIAL_PERF_SQLITE`. For an isolated run, launch the official image: `docker run --rm -it -v "$PWD":/data keinos/sqlite3 sqlite3 /data/perf.db`. Then set `LITEDB_SPATIAL_PERF_SQLITE="Data Source=perf.db"` before running tests. Ensure the `rtree` module is available (SQLite 3.38+ includes it by default).
* **PostGIS**: Supply a connection string through `LITEDB_SPATIAL_PERF_POSTGIS`. A convenient option is the PostGIS docker image: `docker run --rm -d -p 5432:5432 -e POSTGRES_PASSWORD=postgres postgis/postgis`. Connect with `LITEDB_SPATIAL_PERF_POSTGIS="Host=localhost;Username=postgres;Password=postgres"`. The harness will create and drop temporary tables.

### Refreshing Benchmark Snapshots

1. Ensure the required environment variables are exported (LiteDB only needs `LITEDB_SPATIAL_PERF=1`).
2. Run the harness: `dotnet test LiteDB.Spatial.Core.Tests --filter SpatialPerfHarness`. Individual engines can be toggled by setting or omitting their connection string variables.
3. Commit the updated section below. The harness keeps all prose intact and only rewrites the table between the sentinel markers.

## Latest Benchmark Results

<!-- benchmark:begin -->
No benchmark results captured yet.
<!-- benchmark:end -->

The harness writes timestamps in UTC. If you need to regenerate the table without touching code (for example, when updating external database versions), rerun the harness and review the diff before committing.
