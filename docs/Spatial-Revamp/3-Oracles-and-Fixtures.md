# Spatial Testing Oracles & Fixture Packs

The `LiteDB.Spatial.Testing.Oracles` project centralises the heavy-weight dependencies that the spatial test suites rely on for golden answers.  Add a reference from any test project that needs cross-checks against a third-party oracle or wants to load fixtures from `/tests/fixtures`.

## Enabling / disabling oracles

* `SPATIAL_ORACLES` controls which adapters are active.  Leave it unset (default) or set it to `all` to enable every adapter.  Use a comma separated allow-list (e.g. `SPATIAL_ORACLES=geographiclib,mathnet`) to opt into a subset or `off` to disable the adapters entirely.
* `SPATIAL_DB_TESTS=1` must be present for the optional `PostgisOracle` helper.
* Annotate xUnit tests with `[OracleSkip("geographiclib")]` (and similar) to skip gracefully when an adapter is unavailable.  Pass `true` as the first argument to also require `SPATIAL_DB_TESTS=1` for database backed verifications.

## Available adapters

| Adapter | Interface | Package(s) | Use cases |
|---------|-----------|------------|-----------|
| `NtsOracle` | `IGeometryOracle2D` | `NetTopologySuite` + `GeoJSON.Net` | Polygon predicates, anti-meridian helpers |
| `GeographicLibOracle` | `IGeodesicOracle` | `GeographicLib` | High precision great-circle distances |
| `MathNetOracle3D` | `IDistanceOracle3D` | `MathNet.Spatial` | Euclidean distance checks for 3D geometries |
| `PostgisOracle` (optional) | N/A | External PostGIS instance | Differential DB tests, behind `SPATIAL_DB_TESTS` |

Each adapter exposes an `IsEnabled` flag and throws a descriptive exception if you call into it when disabled.  Always probe `IsEnabled` (or guard the test with `OracleSkip`) before issuing expensive operations.

## Fixture packs

All canonical datasets live in `/tests/fixtures`:

* `geodesic_pairs.json` – curated lon/lat pairs with great-circle distances in metres.
* `geojson_polygons/*.json` – GeoJSON features for polygons with holes, self-touches, and anti-meridian spans.
* `point_clouds/*.json` – dense 2D grids and 3D lattices for near / range test coverage.

Use `FixtureLoader` from `LiteDB.Spatial.Core.Tests.Infrastructure` to load them.  It resolves the repo-relative path automatically and deserialises into strongly typed records such as `GeodesicPair` and `PointCloud3D`.  When an assertion fails, call `FailureSnapshot.Write(category, id, payload)` to capture repro JSON under `tests/failures/<category>/` for inspection.

## Tolerances

`SpatialTolerance` exposes consistent tolerances for the two domains we validate:

* Earth geodesics: `1e-4 * expected + 0.05` metres.
* Cartesian (2D/3D): `1e-9 * scale + 1e-9` of the current coordinate magnitude.

It also provides `FormatDelta` helpers so assertion messages include the fixture id, actual delta, and the permitted tolerance.
