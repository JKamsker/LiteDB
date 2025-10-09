# Spatial Upgrade & Migration Checklist

The modular spatial revamp standardises index fields (`_idx`) and bounding boxes (`_mbb`) across the geographic and Cartesian engines. This document outlines the upgrade steps for existing databases that previously relied on the legacy `_gh` field.

## 1. Reference the new façade

Add a project reference to `LiteDB.Spatial` (or reference the NuGet package once published). The façade coordinates metadata persistence, backfills computed fields, and exposes engine-neutral query helpers.

```xml
<ItemGroup>
  <ProjectReference Include="..\LiteDB.Spatial\LiteDB.Spatial.csproj" />
</ItemGroup>
```

## 2. Configure metadata per collection

Call the appropriate `Use*` helper once per collection to persist the engine choice and index options:

```csharp
var places = db.GetCollection<Place>("places");
Spatial.UseGeographic(places, new SpatialIndexOptions(precisionBits: 32));

var assets3D = db.GetCollection<Asset>("assets3d");
var domain3D = BoundingBox.From3D(-100, -100, -100, 100, 100, 100);
Spatial.UseCartesian3D(assets3D, domain3D);
```

`Use*` writes descriptors to the `_spatial_meta` collection so future runs know which engine to materialise.

## 3. Backfill `_idx`/`_mbb`

`Spatial.EnsurePointIndex` reuses the metadata, creates the `$._idx` B-Tree, and populates `_idx` (Morton code) plus `_mbb` (world-space bounding box) fields. Run it once per collection after seeding metadata:

```csharp
Spatial.EnsurePointIndex(places, x => x.Location);
Spatial.EnsurePointIndex(assets3D, x => x.Position);
```

The backfill runner works in batches and updates existing documents in place. If `_idx` already exists with the expected precision the document is skipped.

## 4. Verify descriptors (optional)

Use `SpatialMetadataStore` to inspect the stored descriptor and confirm the geometry field, options, and engine settings:

```csharp
var metadata = new SpatialMetadataStore(db);
var descriptor = metadata.GetRequiredDescriptor("places");
Console.WriteLine(descriptor);
```

Descriptors include the geometry field name, precision, maximum covering cells, and engine-specific settings such as the Cartesian domain or geographic distance mode.

## 5. Clean up legacy state

`Spatial.EnsurePointIndex` writes `_idx` and `_mbb`. Existing `_gh` fields can be dropped at your leisure—the new façade never reads or writes them. Any custom indexes that referenced `$._gh` should be recreated against `$._idx`.

## 6. Update queries

Replace calls to the legacy `LiteDB.Spatial.Spatial` helpers with the new façade:

```diff
- Spatial.Near(places, x => x.Location, center, 5_000);
+ Spatial.Near(places, x => x.Location, center, 5_000);

- Spatial.WithinBoundingBox(places, x => x.Location, -0.2, -0.2, 0.2, 0.2);
+ var bounds = BoundingBox.From2D(-0.2, -0.2, 0.2, 0.2);
+ Spatial.WithinBoundingBox(places, x => x.Location, bounds);
```

The signatures remain familiar but now dispatch to the engine configured in `_spatial_meta` instead of using global options.

## 7. Monitor diagnostics

`SpatialDiagnostics.Explain` turns any `ISpatialQueryPlan` into a human-readable summary, including the number of index ranges, the bounding box field, and the exact predicate description. See [`docs/spatial-diagnostics.md`](spatial-diagnostics.md) for examples.

## 8. Rollout strategy

1. Deploy the new package and run `Use*` + `EnsurePointIndex` for each spatial collection (backfill is idempotent).
2. Update application queries to call the façade.
3. Drop obsolete `_gh` indexes once the new `_idx` coverage is verified.
4. Keep an eye on benchmark baselines in [`docs/spatial-benchmarks.md`](spatial-benchmarks.md) when making precision or coverage tweaks.

Following this checklist migrates existing data sets to the new metadata-driven model without downtime. The façade handles the heavy lifting, leaving application code to focus on domain logic rather than engine plumbing.
