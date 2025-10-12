# Spatial Benchmark Summary

The `LiteDB.Spatial.Core.Tests` perf harness populates this file when `LITEDB_SPATIAL_PERF_CAPTURE=1` is set during `dotnet test`. Run the harness after provisioning the comparison databases to refresh the table below.

| Engine | Duration (ms) | Candidates | Results | Notes |
| --- | --- | --- | --- | --- |
| LiteDB (Cartesian2D) | n/a | - | - | Run the perf harness to capture fresh results. |
| SQLite RTree | skipped | - | - | Requires `LITEDB_SPATIAL_PERF_SQLITE`. |
| PostGIS | skipped | - | - | Requires `LITEDB_SPATIAL_PERF_POSTGRES`. |

_Last generated: placeholder pending first capture_
