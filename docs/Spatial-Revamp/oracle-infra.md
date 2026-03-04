# Spatial Oracle & Fixture Infrastructure

The spatial test suite now consumes a dedicated helper library, **`LiteDB.Spatial.Testing.Oracles`**, which wraps external engines behind thin interfaces so we can cross-check LiteDB's behavior without coupling runtime binaries to third-party dependencies.

## Projects & packages

`LiteDB.Spatial.Testing.Oracles` targets `net8.0` and references:

- [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) for robust planar predicates.
- [GeographicLib](https://geographiclib.sourceforge.io/) for high-precision geodesic distances.
- [MathNet.Spatial](https://numerics.mathdotnet.com/) for Euclidean 3D math.
- [GeoJSON.Net](https://github.com/GeoJSON-Net/GeoJSON.Net) + `Newtonsoft.Json` for fixture parsing.
- [Npgsql](https://www.npgsql.org/) to optionally issue PostGIS queries when `SPATIAL_DB_TESTS` is enabled.

Interfaces exposed by the project:

- `IGeodesicOracle` → distance on the WGS84 ellipsoid.
- `IGeometryOracle2D` → `Contains`/`Within`/`Intersects` using planar coordinates or GeoJSON.
- `IDistanceOracle3D` → Euclidean distance for Cartesian fixtures.

Adapters live under `LiteDB.Spatial.Testing.Oracles/Adapters/` and are intentionally thin. Each one checks the environment through `OracleEnvironment` to avoid running heavyweight integrations when the CI matrix disables them.

## Skipping expensive tests

Use `[OracleFact]` (or its alias `[OracleSkip]`) instead of `[Fact]` whenever a test relies on oracle infrastructure. The attribute reads the following environment variables:

- `SPATIAL_ORACLES`: defaults to `true`; set to `false`, `0`, or `off` to skip all oracle-backed tests.
- `SPATIAL_DB_TESTS`: defaults to `false`; required for PostGIS/DB-backed checks.

Example:

```csharp
[OracleFact(feature: "geodesic_pairs")]
public void VincentyMatchesGeographicLibFixtures()
{
    // …
}
```

## Shared fixtures & loaders

Golden data lives under `/tests/fixtures/`:

- `geodesic_pairs.json` → precomputed WGS84 distances.
- `geojson_polygons/*.json` → polygons with holes and anti-meridian edges.
- `point_clouds/*.json` → 2D and 3D point lattices.

`LiteDB.Spatial.Core.Tests/Infrastructure/FixtureLoader` exposes `Load<T>()`, `LoadCollection<T>()`, and `LoadText()` so tests can read fixtures without worrying about the repo layout. Failures are snapshotted to `/tests/failures` via `FixtureSnapshot` to simplify triage.

## Numeric tolerances

`SpatialTolerance` centralises the two tolerance families used across tests:

- **Earth distances**: `1e-4 * distance + 0.05` meters.
- **Cartesian distances**: `1e-9 * scale + 1e-9`.

Both helpers call `FixtureSnapshot.RecordDistanceFailure` before asserting, guaranteeing that failure messages include fixture IDs, deltas, and tolerances.

## Wiring tests

`LiteDB.Spatial.Core.Tests` now references the oracle project and introduces `SpatialOracleParityTests` which exercise:

- Geographic distance parity against GeographicLib using `geodesic_pairs.json`.
- Cartesian 3D distances against MathNet over `point_clouds/cube_3d.json`.
- GeoJSON polygon semantics against NetTopologySuite.

Future spatial suites can import the same infrastructure by referencing `LiteDB.Spatial.Testing.Oracles` and the infrastructure helpers, then opting into `[OracleFact]` where applicable.
