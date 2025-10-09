# Spatial Guide

LiteDB's modular spatial stack is composed of engine-specific packages
(`LiteDB.Spatial.Geographic`, `LiteDB.Spatial.Cartesian2D`, `LiteDB.Spatial.Cartesian3D`)
and a thin facade (`LiteDB.Spatial`) that orchestrates metadata, backfill, and query
planning. This guide demonstrates how to configure collections, execute index-aware
queries, and surface diagnostics.

## 1. Configure metadata

Spatial metadata is stored per collection inside the `_spatial_meta` collection. Use a
`SpatialMetadataStore` to persist descriptors that describe the engine, geometry field,
dimensionality, and optional settings such as Cartesian domains or geographic distance
modes.

```csharp
extern alias LiteDbBase;

using LiteDB.Spatial;
using BaseLiteDB = LiteDbBase::LiteDB;

using var db = new BaseLiteDB.LiteDatabase("Filename=geo.db");
var metadata = new SpatialMetadataStore(db);

Spatial.UseGeographic(metadata, "places", "location");
Spatial.UseCartesian3D(metadata, "sensors", "position", BoundingBox.From3D(-100, -100, -50, 100, 100, 50));
```

Descriptors are cached in-memory once retrieved, enabling repeated lookups without
re-reading the metadata collection.

## 2. Ensure point indexes

After configuring a collection, call `Spatial.EnsurePointIndex`. The helper creates the
raw `_idx` field, a companion `_mbb` bounding box, and a B-Tree index on `"$._idx"`. The
operation accepts either a database/collection name pair or an existing
`SpatialMetadataStore` and collection reference.

```csharp
var places = db.GetCollection("places");
var descriptor = Spatial.EnsurePointIndex(metadata, places);

// descriptor.Engine exposes the runtime engine instance for diagnostics
Console.WriteLine($"Engine: {descriptor.EngineName}, Dimensions: {descriptor.Dimensions}");
```

`SpatialBackfill` is executed automatically, so existing documents receive `_idx` and
`_mbb` values the first time the helper runs. Subsequent calls update only the documents
that are missing fields or contain stale values.

## 3. Querying with plans

`Spatial.Near` and `Spatial.WithinBoundingBox` return `ISpatialQueryPlan` instances that
describe the Morton ranges to scan, optional covering bounds, and the exact predicate to
apply. Plans can be executed manually by iterating over the index ranges:

```csharp
var center = new GeoPoint(16.37, 48.21);
var plan = Spatial.Near(descriptor, center, radius: 5_000);
var tolerance = descriptor.Options.DistanceTolerance;
var engine = descriptor.Engine!;
var matches = new List<BaseLiteDB.BsonDocument>();
var visited = new HashSet<int>();

foreach (var range in plan.IndexRanges)
{
    var query = places.Query().Where(BaseLiteDB.Query.Between(
        descriptor.Options.IndexFieldName,
        ToIndexValue(range.Start),
        ToIndexValue(range.End)));

    foreach (var doc in query.ToDocuments())
    {
        if (!visited.Add(doc["_id"].AsInt32))
        {
            continue;
        }

        if (!engine.Mapper.TryReadPoint(doc, out GeoPoint point))
        {
            continue;
        }

        if (plan.CoveringBounds.HasValue && !Contains(plan.CoveringBounds.Value, point))
        {
            continue;
        }

        if (engine.Distance.Distance(center, point) <= 5_000 + tolerance)
        {
            matches.Add(doc);
        }
    }
}
```

Helper functions `ToIndexValue` and `Contains` simply convert `ulong` ranges to
`BsonValue` and perform axis-aligned checks. The same pattern applies to three-dimensional
collections by swapping `GeoPoint` for `GeoPoint3D` and using `Contains` overloads that
compare all six bounding-box coordinates.

```csharp
static IEnumerable<(BaseLiteDB.BsonDocument Document, GeoPoint Point)> EnumeratePlan2D(
    SpatialCollectionDescriptor descriptor,
    BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection,
    ISpatialQueryPlan plan)
{
    var engine = descriptor.Engine ?? throw new InvalidOperationException();
    var visited = new HashSet<int>();

    foreach (var range in plan.IndexRanges)
    {
        var query = collection.Query().Where(BaseLiteDB.Query.Between(
            descriptor.Options.IndexFieldName,
            ToIndexValue(range.Start),
            ToIndexValue(range.End)));

        foreach (var document in query.ToDocuments())
        {
            if (!visited.Add(document["_id"].AsInt32))
            {
                continue;
            }

            if (!engine.Mapper.TryReadPoint(document, out GeoPoint point))
            {
                continue;
            }

            if (plan.CoveringBounds.HasValue && !Contains(plan.CoveringBounds.Value, point))
            {
                continue;
            }

            yield return (document, point);
        }
    }
}
```

## 4. Bounding box queries

Bounding-box searches rely on the same infrastructure. Request a plan using
`Spatial.WithinBoundingBox` and filter results using the raw bounding box values stored
in `_mbb` or by re-reading the geometry via the engine mapper.

```csharp
var bounds = BoundingBox.From2D(16.2, 48.0, 17.3, 48.4);
var plan = Spatial.WithinBoundingBox(descriptor, bounds);
var inBox = EnumeratePlan2D(descriptor, places, plan)
    .Where(item => Contains(bounds, item.Point))
    .Select(item => item.Document)
    .ToList();
```

## 5. Diagnostics and explain output

`SpatialDiagnostics.Explain(plan, descriptor)` produces a human-readable summary. Sample
output for a geographic near query:

```
Engine: Geographic (2D)
Index field: _idx
Bounding box field: _mbb
Index ranges (3) via _idx
  - [1208925819614629170, 1208925819614631231] (0x10C0000C00000002 - 0x10C0000C000007FF)
  - [1208925819614637056, 1208925819614639615] (0x10C0000C00001800 - 0x10C0000C00001FFF)
  - [1208925819614643712, 1208925819614646271] (0x10C0000C00003000 - 0x10C0000C000037FF)
Covering bounds via _mbb: [16.32, 48.17, 16.42, 48.25]
Exact predicate: distance <= 5000 m
```

The explain output is ideal for logging and regression tests: range counts highlight
Morton cover sizes, while bounding boxes and exact predicates confirm the planner is
applying index-aware pruning.

## 6. LINQ integration

The `LiteDB.Spatial.Core` package ships `SpatialExpressions` and `SpatialResolver`. When
a resolver is registered with the queryable pipeline, expressions such as
`doc => SpatialExpressions.Near(doc.Location, target, radius)` are translated into the
same plans described above. Index ranges are injected into the expression tree, ensuring
queries remain index-aware even when composed via LINQ.

---

The combination of metadata descriptors, automatic backfill, and diagnostic-friendly
plans lets you mix geographic, Cartesian 2D, and Cartesian 3D collections in the same
application without juggling global singletons or bespoke index fields.
