# Spatial Upgrade Guide

This guide walks existing applications from the legacy in-core spatial helpers (`Spatial.Use*`, `_gh` indexes) to the plugin-based interceptor that ships with LiteDB.Spatial. The migration can be performed per collection and does not require downtime when executed carefully.

## 1. Identify collections that need migration

Legacy builds stored Morton values in a `_gh` field and bounding boxes in `_mbb`. Scan each collection to determine whether it still relies on the old schema:

```csharp
using var db = new LiteDatabase("Filename=data.db;Mode=Exclusive");
var raw = db.GetCollection("your_collection");

var hasLegacyFields = raw.Find(Query.Exists("_gh")).Any();
```

If either `_gh` or `_mbb` exists and the collection should remain spatial, continue with the migration steps.

## 2. Declare spatial options and enable the plugin

Annotate spatial members (or register them with the mapper) so the interceptor knows which engine to use when `EnsureIndex` runs:

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
    .WithSpatialOptions(p => p.Location, options =>
    {
        options.WithDistanceTolerance(15);
        options.WithIndexFieldName("_idx");
        options.WithBoundingBoxFieldName("_mbb");
    });
```

Then create the database with the spatial plugin registered:

```csharp
using LiteDB;
using LiteDB.Plugins;
using LiteDB.Spatial;

var connection = new ConnectionString("Filename=data.db;Mode=Exclusive");
using var db = new LiteDatabase(connection, plugins: new ILitePlugin[]
{
    new SpatialPlugin()
});
```

- `GeoPoint` members default to the geographic engine, but Cartesian datasets **must** supply a domain via `options.WithDomain(...)` or the migration will stop with a warning.
- Existing `_spatial_meta` documents are discovered automatically; attributes are only required for collections that were never configured.

## 3. Provision metadata via `EnsureIndex`

Once options are declared and the plugin is active, call `EnsureIndex` on each spatial field:

```csharp
var places = db.GetCollection<Place>("places");
places.EnsureIndex(p => p.Location); // Interceptor provisions metadata and backfills indexes.
```

During interception the plugin:

- Writes or updates the descriptor in `_spatial_meta`.
- Creates the `_idx` Morton column and `_mbb` bounding box column when missing.
- Backfills existing documents, replacing `_gh` usage transparently.

Monitor the logger output; warnings indicate missing configuration (for example, an undefined Cartesian domain) and the interceptor will fall back to the core implementation instead of mutating data.

## 4. Validate the migration

Check metadata and run a query using the new LINQ extensions:

```csharp
var meta = db.GetCollection("_spatial_meta").FindById("places");
Console.WriteLine(meta);

var center = new GeoPoint(16.3738, 48.2082);
var hits = places.Query()
    .WhereNear(p => p.Location, center, radius: 5_000)
    .ToList();
```

- Descriptors should list the expected engine, precision, index, and bounding box field names.
- `WhereNear` emits a descriptive exception if the plugin is not registered or `EnsureIndex` never executed, making smoke tests straightforward.

## 5. Clean up legacy fields (optional)

After verifying that queries use the plugin-provisioned metadata, remove obsolete `_gh` values to reduce document size:

```csharp
raw.UpdateMany("{ _gh: DELETE }", "_gh != null");
```

Keep `_mbb`; the new engines reuse it for bounding boxes.

## 6. Automate large migrations

Create a migration runner to iterate collections safely:

```csharp
foreach (var name in db.GetCollectionNames())
{
    var collection = db.GetCollection(name);
    if (collection == null || name.StartsWith("_"))
    {
        continue;
    }

    collection.EnsureIndex(BsonExpression.Create("$.Location"));
}
```

- For strongly typed repositories, prefer `db.GetCollection<YourType>(name).EnsureIndex(x => x.Location);` so the mapper provides attribute data.
- The interceptor skips collections that already expose compatible metadata, making repeated runs idempotent.

## 7. Rollback considerations

Because the plugin only adds `_spatial_meta`, `_idx`, and `_mbb`, rolling back to an older LiteDB build merely requires re-enabling legacy helpers. If `_gh` was removed, calling `Spatial.EnsurePointIndex` in the previous release will reintroduce it.

## 8. Troubleshooting

- **Missing descriptor warnings:** Confirm that annotations or mapper configuration ran before `EnsureIndex`. Cartesian datasets must supply a domain.
- **No plugin registered:** `WhereNear` throws a `LiteException` that reminds you to add `new SpatialPlugin()` to the `LiteDatabase` constructor.
- **Unexpected range results:** Use the [spatial diagnostics guide](spatial-diagnostics.md) to capture explain output and verify cover cell counts and distance tolerances.

Following these steps ensures collections created before the plugin split continue to work with the new interceptor-based flow while modernizing query code to use `WhereNear` and related extensions.
