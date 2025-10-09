# Spatial Upgrade Guide

The spatial revamp introduces engine-specific metadata, deterministic index fields, and
plan diagnostics. Existing databases that relied on `_gh`/`_geoIndex` fields can migrate
in-place by persisting collection descriptors and backfilling the new `_idx` and `_mbb`
fields. This guide walks through the recommended upgrade workflow.

## 1. Persist collection metadata

Create a `SpatialMetadataStore` for the database and register the engine that should
serve each collection. Geographic collections use longitude/latitude pairs while
Cartesian collections operate on arbitrary units.

```csharp
extern alias LiteDbBase;

using LiteDB.Spatial;
using BaseLiteDB = LiteDbBase::LiteDB;

using var db = new BaseLiteDB.LiteDatabase(path);
var metadata = new SpatialMetadataStore(db);

Spatial.UseGeographic(metadata, "places", "location");
Spatial.UseCartesian3D(metadata, "sensors", "position", BoundingBox.From3D(-100, -100, -50, 100, 100, 50));
```

Each call stores a `SpatialCollectionDescriptor` in the `_spatial_meta` collection.
Descriptors capture the engine name, dimensionality, and options such as
`PrecisionBits`, `MaxCoveringCells`, and custom field names. The metadata allows the
planner to validate documents and dispatch queries to the correct engine.

## 2. Backfill `_idx` and `_mbb`

Once metadata has been persisted, run `Spatial.EnsurePointIndex` to create indexes,
compute Morton codes, and hydrate bounding boxes. The helper automatically creates a
B-Tree on `"$._idx"` (or your customized field name) alongside the raw `_idx` and `_mbb`
fields.

```csharp
var places = db.GetCollection("places");
var descriptor = Spatial.EnsurePointIndex(metadata, places);

// descriptor.Engine is populated and can be reused for diagnostics or manual filtering
```

The operation is idempotent—rerunning it updates new or changed documents without
mutating rows that already contain the expected values. Collections configured for 3D
engines receive six-value `_mbb` arrays; 2D collections receive four-value arrays.

## 3. Replace legacy field usage

New queries should rely on `_idx` and `_mbb`. Remove any writes to `_gh`, ensure models
no longer expose the legacy properties, and update application-side migrations to check
for the presence of `_idx`. The `SpatialMetadataStore.ValidateDocument` helper can be
used during import pipelines to detect stale documents before they reach production.

## 4. Update query code

Use `Spatial.Near`, `Spatial.WithinBoundingBox`, or the LINQ-facing
`SpatialExpressions` API to generate plans. The planner reads the persisted metadata,
creates Morton range covers, and returns an `ISpatialQueryPlan` that can be used with
`SpatialDiagnostics.Explain` or executed manually.

```csharp
var plan = Spatial.Near(descriptor, new GeoPoint(16.37, 48.21), radius: 5_000);
var explain = SpatialDiagnostics.Explain(plan, descriptor);
Console.WriteLine(explain);
```

LINQ queries that invoke `SpatialExpressions.Near`/`InBox` continue to work once the
application registers a `SpatialResolver` that feeds descriptors to the translator.

## 5. Verify with diagnostics

After migrating each collection, run a sample query and inspect the explain output. A
healthy plan lists one or more `_idx` ranges, non-empty covering bounds, and an exact
predicate such as `distance <= 5000 m`. This ensures the new metadata and computed
fields are being exercised.

## Field summary

| Field | Purpose | Notes |
| ----- | ------- | ----- |
| `_idx` | Morton-encoded index key | Stored as `Int64` or `Decimal`; indexed twice (raw field and `"$._idx"`). |
| `_mbb` | Axis-aligned bounding box | 4 values for 2D, 6 values for 3D. Used for coarse pruning and diagnostics. |
| `_spatial_meta` | Collection metadata | Stores `SpatialCollectionDescriptor` documents. |

Upgrades are incremental: configure one collection, backfill, verify diagnostics, then
move on to the next. Applications can continue operating during the process because the
helper batches writes and respects existing `_id` ordering.
