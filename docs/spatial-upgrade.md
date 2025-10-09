# Spatial Upgrade Guide

This guide covers migrating collections that previously relied on the legacy `_gh` index to the new modular spatial stack. The process is safe for live databases and can be performed incrementally per collection.

## 1. Identify spatial collections

Legacy applications typically exposed a `_gh` numeric index and `_mbb` bounding box arrays on documents. Start by checking your collections for those fields:

```csharp
using var db = new LiteDatabase("Filename=data.db;Mode=Exclusive");
var raw = db.GetCollection("your_collection");

var hasLegacyFields = raw.Find(Query.Exists("_gh")).Any();
```

If the fields are present, the collection should be migrated.

## 2. Configure metadata

The new facade persists metadata in `_spatial_meta`. For a geographic dataset migrate with:

```csharp
var places = db.GetCollection<Place>("places");
Spatial.UseGeographic(places, x => x.Location);
```

For Cartesian datasets choose the appropriate helper and domain:

```csharp
Spatial.UseCartesian2D(points2D, x => x.Position, BoundingBox.From2D(-10_000, -10_000, 10_000, 10_000));
Spatial.UseCartesian3D(points3D, x => x.Position, BoundingBox.From3D(-100, -100, -100, 100, 100, 100));
```

`Use*` stores engine information, precision, and optional distance modes. It also creates the `_idx` and `_mbb` indexes so future queries are automatically routed through the new planner.

Morton encoding for three-dimensional indexes fits inside 64-bit keys when each axis uses
21 precision bits. The facade clamps higher values automatically so existing code can keep
passing `SpatialIndexOptions` that were tuned for 2D datasets.

## 3. Backfill `_idx` and `_mbb`

If documents already contained `_gh`, you can safely rebuild the new fields by calling the engine-neutral helper:

```csharp
Spatial.EnsurePointIndex(places);
```

The helper reads the metadata, instantiates the correct engine, and replays the backfill pipeline. Documents keep any legacy `_gh` fields until you remove them, so existing consumers can continue to read the old values during the transition.

## 4. Validate

Inspect the `_spatial_meta` collection to confirm the descriptor matches expectations:

```csharp
var meta = db.GetCollection("_spatial_meta").FindById("places");
Console.WriteLine(meta);
```

A typical document contains:

```json
{
  "_id": "places",
  "engine": "Geographic2D",
  "dimensions": 2,
  "geometryField": "Location",
  "options": {
    "precisionBits": 40,
    "indexFieldName": "_idx",
    "boundingBoxFieldName": "_mbb"
  },
  "engineSettings": {
    "distanceMode": "Vincenty"
  }
}
```

Run a smoke test to confirm range scans work as expected:

```csharp
var hits = Spatial.Near(places, x => x.Location, new GeoPoint(16.3738, 48.2082), 5_000);
```

## 5. Clean up legacy fields (optional)

Once your application has switched to the new helpers you may remove the `_gh` field to avoid confusion:

```csharp
raw.UpdateMany("{ _gh: DELETE }", "_gh != null");
```

Keep `_mbb` because it is shared with the new engine.

## 6. Automating migrations

For large databases consider creating a lightweight migration runner:

```csharp
var store = new SpatialMetadataStore(db);

foreach (var collectionName in db.GetCollectionNames())
{
    if (!store.TryGetDescriptor(collectionName, out _))
    {
        var typed = db.GetCollection<BsonDocument>(collectionName);
        // Decide which engine to use based on your domain knowledge
        Spatial.UseGeographic(typed, "Location");
        Spatial.EnsurePointIndex(typed);
    }
}
```

This pattern leaves collections untouched when metadata is already present, making it safe to run during deployment.

## 7. Rollback considerations

Because the new fields are additive and metadata lives in a separate collection, rolling back to an older build simply means ignoring `_idx` and `_spatial_meta`. If you removed `_gh`, re-running `Spatial.EnsurePointIndex` in the old version will recreate it.

## 8. Verify backups

Before migrating production data ensure backups are up to date. The backfill process only touches the target collection and `_spatial_meta`, but being able to restore gives you confidence while testing.

## 9. Troubleshooting

If `Spatial.EnsurePointIndex` throws a `SpatialMetadataException`:

* Confirm the `Use*` helper was called for the collection.
* Check that the geometry expression resolves to the correct property name.
* For Cartesian engines ensure the domain describes two or three dimensions as expected.

Refer to the [diagnostics guide](spatial-diagnostics.md) for explain output examples that help verify range coverage and exact predicates.

