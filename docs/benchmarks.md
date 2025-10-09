# Spatial Benchmark Harness

The spatial test suites measure how well LiteDB preserves locality, validates LINQ explain plans, and compares to external engines. The sections below describe how to run each group and how to refresh recorded benchmark snapshots.

## Locality metrics

The locality fixture encodes deterministic grids and verifies that Morton encodings retain their nearest neighbours.

```bash
dotnet test LiteDB.Spatial.Core.Tests --filter FullyQualifiedName~LocalityTests
```

## LINQ explain parity

These tests ensure that LINQ query plans surface `_idx` and `_mbb` predicates before exact filters.

```bash
dotnet test LiteDB.Spatial.Core.Tests --filter FullyQualifiedName~SpatialExplainIntegrationTests
```

## Performance comparisons

The optional performance harness reuses the locality fixtures to compare candidate reduction across LiteDB, SQLite RTree, and PostGIS.

1. Enable the harness:

   ```bash
   export LITEDB_SPATIAL_PERF=1
   ```

2. (Optional) Override the SQLite connection string:

   ```bash
   export LITEDB_SPATIAL_SQLITE="Data Source=spatial-benchmarks.db"
   ```

3. (Optional) Provide a PostGIS endpoint (requires the PostGIS extension):

   ```bash
   export LITEDB_SPATIAL_POSTGIS="Host=localhost;Username=postgres;Password=postgres;Database=postgres"
   ```

   A local instance can be started with Docker:

   ```bash
   docker run --rm -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgis/postgis:16-3.4
   ```

4. To update the Markdown snapshot, also set:

   ```bash
   export LITEDB_SPATIAL_PERF_WRITE=1
   ```

5. Run the harness:

   ```bash
dotnet test LiteDB.Spatial.Core.Tests --filter Category=Perf
   ```

### Benchmark snapshot

The following section is rewritten when `LITEDB_SPATIAL_PERF_WRITE=1` is specified. Do not edit between the markers.

<!-- benchmarks:start -->
<!-- benchmarks:end -->
