# Spatial Query Benchmark Baselines

The spatial facade benchmarks were executed with the following command to capture the baseline numbers for regression monitoring:

```
dotnet run --project LiteDB.Benchmarks/LiteDB.Benchmarks.csproj -c Release -- --spatial-only --job short --filter "*DatasetSize: 1000, ConnectionType: Direct, Password: null*"
```

This targets the `SpatialQueryBenchmarks` suite at a dataset size of 1,000 records (direct connection, no password) using BenchmarkDotNet's *ShortRun* job. The run completes in roughly five minutes and produces both the default job and the short-run summary below. The default job (first block) is the baseline we track in CI.

| Query | Mean (ms) | Error (ms) | StdDev (ms) | Allocated (MB) |
| --- | ---: | ---: | ---: | ---: |
| NearQuery | 11.031 | 0.2203 | 0.4647 | 6.97 |
| BoundingBoxQuery | 10.677 | 0.2119 | 0.5157 | 6.54 |
| PolygonContainmentQuery | 10.420 | 0.2083 | 0.5187 | 7.24 |
| RouteIntersectionQuery | 10.196 | 0.2018 | 0.4834 | 7.13 |

## Notes

* The `--spatial-only` flag now injects a BenchmarkDotNet filter so only `SpatialQueryBenchmarks` cases execute, dramatically reducing the benchmark surface area compared to the previous run-all behavior.
* Additional CLI filters are honored and combined with the spatial filter. Use `--filter` patterns (as shown above) to focus on specific dataset sizes or connection permutations.
* For a quicker sanity check you can swap `--job short` with `--job dry`; this executes a single iteration while still respecting the spatial filter, but is not recommended for collecting regression baselines.
