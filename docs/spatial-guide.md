# Spatial Indexing Guide

LiteDB’s spatial plugin intercepts `EnsureIndex` to configure metadata, build spatial indexes, and keep query plans transparent. Use this guide to declare spatial options, index collections, and execute queries with the new opt-in module.

## 1. Enable the Plugin and Declare Options

```csharp
using LiteDB;
using LiteDB.Plugins;
using LiteDB.Spatial;

var connection = new ConnectionString("Filename=places.db;Mode=Shared");
using var db = new LiteDatabase(connection, plugins: new ILitePlugin[]
{
    new SpatialPlugin()
});
```

Tell the interceptor how to provision metadata by annotating your entity or registering options with the mapper:

```csharp
public sealed class Place
{
    public int Id { get; set; }

    [SpatialOptions(
        Engine = SpatialEngineKind.Geographic2D,
        PrecisionBits = 40,
        DistanceMode = GeographicDistanceMode.Vincenty)]
    public GeoPoint Location { get; set; } = default!;
}

BsonMapper.Global.Entity<Place>()
    .WithSpatialOptions(x => x.Location, options =>
    {
        options.WithDistanceTolerance(15);
        options.WithIndexFieldName("_idx");
        options.WithBoundingBoxFieldName("_mbb");
    });
```

- Geographic datasets default to the geographic engine when a `GeoPoint` member is discovered.
- Cartesian datasets must declare a domain via `options.WithDomain(...)` (2D or 3D) to build valid Morton keys.
- Existing `_spatial_meta` descriptors are reused automatically—attributes and mapper overrides only apply to new collections.

## 2. Provision Indexes with `EnsureIndex`

```csharp
var places = db.GetCollection<Place>("places");

// Spatial interceptor provisions metadata, backfills indexes, and caches descriptors.
places.EnsureIndex(p => p.Location);
```

- `EnsureIndex` logs warnings if configuration is incomplete (e.g., missing domain for Cartesian datasets) and falls back to the core implementation instead of mutating data blindly.
- The plugin writes metadata to `_spatial_meta`, creates `_idx` and `_mbb` fields, and backfills existing documents for you.
- Legacy helpers such as `Spatial.UseGeographic` remain available for fine-grained control, but the interceptor is the preferred path.

## 3. Querying Data

### Radius searches

```csharp
var center = new GeoPoint(16.3738, 48.2082); // (longitude, latitude)

var withinFiveKilometers = places.Query()
    .WhereNear(p => p.Location, center, radius: 5_000,
        distanceMode: GeographicDistanceMode.Haversine)
    .ToList();
```

- `WhereNear` composes the spatial predicate, allowing the plugin’s query planner to pick the best index strategy.
- String and `BsonExpression` overloads exist when the geometry path is resolved at runtime.
- Prefer the extension methods for readability; `Spatial.Near` remains for imperative callers.

### Bounding boxes

```csharp
var bounds = BoundingBox.From2D(170, -10, 190, 10); // crosses the anti-meridian

var crossing = places.Query()
    .WhereWithinBox(p => p.Location, bounds)
    .ToList();
```

`WhereWithinBox` handles two- and three-dimensional domains and respects anti-meridian wrapping for geographic datasets.

## 4. LINQ & Expressions

- The plugin registers LINQ resolver factories, so `LiteDatabase.Query()` pipelines automatically resolve spatial members once `EnsureIndex` has provisioned metadata.
- `SpatialExpressions` continues to expose low-level helpers for advanced scenarios, but extension methods cover common predicates without manual resolver wiring.
- When no descriptor is found, the plugin produces a descriptive `LiteException` pointing back to `EnsureIndex`.

## 5. Inspecting Plans

Inspect how a query executes by combining the planner output with the diagnostics helpers:

```csharp
var store = new SpatialMetadataStore(db);
var descriptor = store.GetRequiredDescriptor("places");

var explain = SpatialDiagnostics.Explain(
    SpatialGeographic.Near(descriptor, center, 5_000),
    descriptor);

Console.WriteLine(explain);
```

See [spatial-diagnostics.md](spatial-diagnostics.md) for a detailed breakdown of each planning stage.

## 6. Sample API

`samples/SpatialApiSample` showcases the full flow:

```bash
dotnet run --project samples/SpatialApiSample
curl -X POST "http://localhost:5000/seed"
curl "http://localhost:5000/places/near?lat=48.2&lon=16.37&radiusKm=5"
```

- `/seed` registers the plugin, annotates the entity, and calls `EnsureIndex` to provision metadata.
- `/places/near` and `/places/within` rely on `WhereNear`/`WhereWithinBox`, ensuring the sample stays aligned with production guidance.

## 7. Next steps

- Follow the [upgrade guide](spatial-upgrade.md) to migrate collections created before the plugin era.
- Capture baselines with the refreshed `SpatialQueryBenchmarks` suite (see [spatial-benchmarks.md](spatial-benchmarks.md)).
- Keep a metadata snapshot handy by querying `_spatial_meta` for troubleshooting.

