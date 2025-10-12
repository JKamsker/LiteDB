# Spatial Indexing Guide

LiteDB's spatial revamp introduces engine-specific configuration helpers, metadata-driven dispatch, and diagnostics that keep query plans transparent. This guide walks through configuring collections, executing queries, and inspecting plans using the new facade.

## 1. Configuring collections

### Geographic (WGS84)

```csharp
using LiteDB;
using LiteDB.Spatial;

using var db = new LiteDatabase("Filename=places.db;Mode=Shared");
var places = db.GetCollection<Place>("places");

// Persist metadata, create indexes, and backfill existing documents.
Spatial.UseGeographic(places, x => x.Location,
    options: new SpatialIndexOptions(precisionBits: 40),
    distanceMode: GeographicDistanceMode.Vincenty);
```

`UseGeographic` stores the engine choice, precision, and distance mode in `_spatial_meta`, builds the `_idx` and `_mbb` indexes, and runs the backfill utility so existing documents pick up the new fields.

### Cartesian engines

Flat coordinate systems use the `UseCartesian*` helpers and must supply a domain describing the valid coordinate range:

```csharp
var measurements2D = db.GetCollection<SensorReading>("grid");
Spatial.UseCartesian2D(measurements2D, r => r.Position, BoundingBox.From2D(-1000, -1000, 1000, 1000));

var measurements3D = db.GetCollection<Point3DRecord>("cloud");
Spatial.UseCartesian3D(measurements3D, r => r.Position, BoundingBox.From3D(-50, -50, -50, 50, 50, 50));
```

> **Note:** Three-dimensional Morton keys fit within 64 bits when each axis uses at
> most 21 precision bits. Higher values are automatically clamped to keep range
> scans accurate.

After configuration you can call `Spatial.EnsurePointIndex(collection)` whenever you need to rebuild the computed fields (for example after bulk imports that bypassed the mapper). The helper reads the metadata, instantiates the correct engine, and replays the backfill.

## 2. Querying data

### Radius searches

`Spatial.Near` combines Morton range scans with `_mbb` filtering before falling back to exact distance checks using the configured engine.

```csharp
var center = new GeoPoint(16.3738, 48.2082); // (longitude, latitude)
var withinFiveKilometers = Spatial.Near(places, x => x.Location, center, radius: 5_000);
```

Results are sorted by distance and the tolerance configured in `SpatialIndexOptions` is honoured automatically.

### Bounding boxes

Bounding-box queries rely on the engine's covering logic and support anti-meridian ranges out of the box:

```csharp
var bounds = BoundingBox.From2D(170, -10, 190, 10); // crosses the anti-meridian
var crossing = Spatial.WithinBoundingBox(places, x => x.Location, bounds);
```

For Cartesian datasets the same API accepts both 2D and 3D boxes created via `BoundingBox.From2D`/`From3D`.

## 3. LINQ & expressions

The `LiteDB.Spatial.Core` package still exposes `SpatialExpressions` for LINQ queries. Once a collection is configured, register the descriptors with the resolver and issue LINQ queries that reference the expression helpers:

```csharp
var resolver = new SpatialResolver(new[]
{
    new SpatialMetadataStore(db).GetRequiredDescriptor("places")
});

var query = places.Query()
    .Where(p => SpatialExpressions.Near(p.Location, center, 5_000))
    .ToList();
```

The metadata-driven planner keeps index usage consistent whether you issue imperative or LINQ-based queries.

## 4. Inspecting plans

To see how a query will execute, call `SpatialDiagnostics.Explain(plan, descriptor)` after building a plan via the engine-specific helpers:

```csharp
var store = new SpatialMetadataStore(db);
var descriptor = store.GetRequiredDescriptor("places");
var engine = SpatialDiagnostics.Explain(
    SpatialGeographic.Near(descriptor, center, 5_000),
    descriptor);

Console.WriteLine(engine);
```

A dedicated [Diagnostics reference](spatial-diagnostics.md) dives deeper into the explain output structure.

## 5. Sample API

`samples/SpatialApiSample` wires everything together with minimal endpoints:

```bash
dotnet run --project samples/SpatialApiSample
curl -X POST "http://localhost:5000/seed"
curl "http://localhost:5000/places/near?lat=48.2&lon=16.37&radiusKm=5"
```

The `/seed` endpoint calls `Spatial.UseGeographic` to configure the collection, and the `/places/near` and `/places/within` endpoints call the top-level helpers shown above.

## 6. Next steps

* Read the [upgrade guide](spatial-upgrade.md) to migrate existing collections that relied on `_gh`.
* Capture baselines with the refreshed `SpatialQueryBenchmarks` suite (see [benchmarks](spatial-benchmarks.md)).
* Keep a metadata snapshot handy by querying the `_spatial_meta` collection:

```csharp
var meta = db.GetCollection("_spatial_meta").FindAll().ToList();
```

This helps confirm options, distance modes, and domains before running troubleshooting commands.

