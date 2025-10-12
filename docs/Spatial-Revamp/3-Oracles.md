# Spatial oracle harness

The `LiteDB.Spatial.Testing.Oracles` project collects every third-party library we lean on to validate LiteDB's spatial
behaviour. The assembly is **test-only** and can be referenced from any project under `tests/`.

## Features

| Area | Adapter | Notes |
| ---- | ------- | ----- |
| Geodesic distances | `GeographicLibOracle` | Calls GeographicLib when available, falls back to an internal Haversine implementation. |
| 2D geometry | `NtsOracle` | Wraps NetTopologySuite polygons for area/perimeter checks. |
| 3D Euclidean | `MathNetOracle3D` | Leverages MathNet.Spatial's `Point3D` distance. |
| Database parity | `PostgisOracle` | Placeholder guarded by `SPATIAL_DB_TESTS`; throws unless an external DB harness lights it up. |

Access the adapters via `SpatialOracleCatalog`. Each implementation exposes `Name` and `IsAvailable` so you can filter before
running a test matrix.

```csharp
foreach (var oracle in SpatialOracleCatalog.Geodesic.Where(SpatialOracleCatalog.ShouldExecute))
{
    var distance = oracle.DistanceMeters(pair.From, pair.To);
    SpatialTolerance.AssertWithinEarthDistance(pair.Id, expected, distance, oracle.Name);
}
```

## Environment toggles

* `SPATIAL_ORACLES`: comma-separated list of adapters (`nts`, `geographiclib`, `mathnet-3d`, `postgis`). Use `*` (default) to run
  all available options.
* `SPATIAL_DB_TESTS`: opt-in flag for database-backed comparisons. Accepts `1/0`, `true/false`, `yes/no`.

Annotate long-running tests with `OracleSkipAttribute` to honour these variables automatically:

```csharp
[Fact, OracleSkip(OracleNames.Nts)]
public void Polygon_area_matches_nts()
{
    // test body
}

[Fact, OracleSkip(true, OracleNames.Postgis)]
public void Near_query_matches_postgis()
{
    // requires SPATIAL_DB_TESTS=1
}
```

## Fixtures & tolerances

Fixture packs live under `/tests/fixtures/` and can be loaded with the helpers inside `LiteDB.Spatial.Core.Tests/Infrastructure/`:

```csharp
var pairs = SpatialFixtureLoader.LoadGeodesicPairs();
var polygons = SpatialFixtureLoader.LoadPolygons();
var pointClouds = SpatialFixtureLoader.LoadPointClouds();
```

Use `SpatialTolerance.AssertWithinEarthDistance`/`AssertWithinCartesianDistance` to compare outputs. Whenever a tolerance is
exceeded a JSON snapshot is written to `TestResults/SpatialSnapshots/<category>/` with the offending values and fixture ID.

