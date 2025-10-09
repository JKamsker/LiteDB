# Spatial Revamp Usage Guide

LiteDB's modular spatial stack exposes a single façade in `LiteDB.Spatial.Spatial`. The façade wires engine metadata, backfills computed fields, and emits query plans that keep scans tight. This guide walks through configuring collections for the geographic and Cartesian engines, issuing queries, and validating metadata.

## 1. Configure collections

### Geographic (WGS84)

```csharp
using LiteDB;
using LiteDB.Spatial;

using var db = new LiteDatabase("Filename=places.db;Mode=Shared");
var places = db.GetCollection<Place>("places");

Spatial.UseGeographic(places, new SpatialIndexOptions(precisionBits: 32));
Spatial.EnsurePointIndex(places, p => p.Location);
```

`UseGeographic` persists `{ engine: Geographic2D, dimensions: 2 }` metadata in `_spatial_meta`. `EnsurePointIndex` then backfills `_idx` and `_mbb`, creates the `$._idx` B-Tree, and attaches a runtime `GeographicEngine` to the descriptor.

### Cartesian 2D

```csharp
var sensors2D = db.GetCollection<Cartesian2DReading>("sensors2d");
var domain2D = BoundingBox.From2D(-50, -50, 50, 50);

Spatial.UseCartesian2D(sensors2D, domain2D, new SpatialIndexOptions(precisionBits: 24));
Spatial.EnsurePointIndex(sensors2D, s => s.Position);
```

Cartesian engines require an explicit domain so the encoder can normalize coordinates. Defaults mirror `_idx`/`_mbb`, so the same indexers work across engines.

### Cartesian 3D

```csharp
var sensors3D = db.GetCollection<Cartesian3DReading>("sensors3d");
var domain3D = BoundingBox.From3D(-100, -100, -100, 100, 100, 100);

Spatial.UseCartesian3D(sensors3D, domain3D, new SpatialIndexOptions(precisionBits: 28));
Spatial.EnsurePointIndex(sensors3D, s => s.Position);
```

Three dimensional collections default to a slightly higher precision so Morton ranges stay tight even as the z-axis grows.

## 2. Query with range pruning

### Radius searches (`Near`)

```csharp
var center = new GeoPoint(16.3738, 48.2082); // lon, lat
var radiusMeters = 5_000;
var hits = Spatial.Near(places, p => p.Location, center, radiusMeters).ToList();
```

The façade consults persisted metadata, requests a plan from the configured engine, executes the `$._idx` range scan, applies `_mbb` prefilters, and finally evaluates the engine-specific distance predicate. Results arrive sorted by distance; pass an optional `limit` to cap the list.

### Bounding boxes (`WithinBoundingBox`)

```csharp
var bounds = BoundingBox.From2D(16.0, 48.0, 17.0, 48.4);
var window = Spatial.WithinBoundingBox(places, p => p.Location, bounds).ToList();
```

Bounding boxes work for two and three dimensional collections. The façade validates dimensionality, emits index predicates, and applies final coordinate checks to guarantee accuracy even when the covering bounds were widened.

## 3. Inspect metadata

`SpatialMetadataStore` persists descriptors in `_spatial_meta` and caches them for reuse:

```csharp
var metadata = new SpatialMetadataStore(db);
var descriptor = metadata.GetRequiredDescriptor("places");

Console.WriteLine($"Engine: {descriptor.EngineName}");
Console.WriteLine($"Geometry field: {descriptor.GeometryFieldName}");
Console.WriteLine($"Index field: {descriptor.Options.IndexFieldName}");
```

Descriptors record the engine name, dimensionality, geometry field, options, and any engine-specific settings (distance mode for geographic indices or domains for Cartesian ones). `EnsurePointIndex` refreshes the descriptor whenever options change.

## 4. Diagnostics

Each engine exposes query plans through the shared diagnostics helper. Combine the descriptor with a plan and call `SpatialDiagnostics.Explain` to get human-readable output:

```csharp
var plan = SpatialGeographic.Near(descriptor, center, radiusMeters);
var explain = SpatialDiagnostics.Explain(plan, descriptor);
Console.WriteLine(explain);
```

You can find more detail on interpreting explains in [`docs/spatial-diagnostics.md`](spatial-diagnostics.md).

## 5. Samples

* `samples/SpatialApiSample` – a minimal API that seeds geographic data and exposes `/places/near` plus `/places/within` endpoints.
* `samples/Spatial3DSample` – a console walkthrough of Cartesian 3D indexing and queries.

Both projects target `net8.0` and reference the new `LiteDB.Spatial` package so you can copy-paste snippets into your own applications quickly.

## 6. Tips

* Always call the appropriate `Use*` method before `EnsurePointIndex` so metadata captures the intended engine.
* The façade stores `_idx` values as integers (`long`/`decimal` fallback). Use `$._idx` in custom expressions when building manual queries.
* Bounding boxes are always stored in world coordinates (`_mbb`) to keep diagnostics readable. The façade validates shape length to avoid dimension mismatches.
* When migrating from legacy `_gh` fields, run `Spatial.EnsurePointIndex` once per collection—the backfill process repopulates `_idx`/`_mbb` in batches.

With these building blocks you can move between geographic and Cartesian datasets without rewriting pipelines: persist metadata once, and let the engines handle range planning for you.
