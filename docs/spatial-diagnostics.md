# Spatial Diagnostics

Every spatial engine produces an `ISpatialQueryPlan` that describes the index ranges, covering bounds, and exact predicate used for a query. `LiteDB.Spatial.SpatialDiagnostics` turns those plans into human-readable reports so you can confirm that indexes are engaged and pruning behaves as expected.

## 1. Collect a descriptor and plan

Start by loading the persisted descriptor for the collection and requesting a plan from the relevant engine:

```csharp
using LiteDB;
using LiteDB.Spatial;

using var db = new LiteDatabase("Filename=places.db;Mode=Shared");
var metadata = new SpatialMetadataStore(db);
var descriptor = metadata.GetRequiredDescriptor("places");

var center = new GeoPoint(16.3738, 48.2082);
var radius = 2_500d;
var plan = descriptor.EngineName switch
{
    GeographicEngine.EngineName => SpatialGeographic.Near(descriptor, center, radius),
    Cartesian2DEngine.EngineName => SpatialCartesian2D.Near(descriptor, center, radius),
    Cartesian3DEngine.EngineName => SpatialCartesian3D.Near(descriptor, new GeoPoint3D(0, 0, 0), radius),
    _ => throw new InvalidOperationException("Unknown engine")
};
```

Engine facades (`SpatialGeographic`, `SpatialCartesian2D`, `SpatialCartesian3D`) validate the descriptor, ensure an engine is attached, and build a plan without executing the query.

## 2. Explain the plan

```csharp
var explain = SpatialDiagnostics.Explain(plan, descriptor);
Console.WriteLine(explain);
```

### Sample output

```
Engine: Geographic2D (2D)
Index field: _idx
Bounding box field: _mbb
Index ranges (3) via _idx
  - [10347957530557214720, 10347957531557214720] (0x8F9F2B4CA0000000 - 0x8F9F2B4CC0000000)
  - [10347957533557214720, 10347957534557214720] (0x8F9F2B4CE0000000 - 0x8F9F2B4D00000000)
  - [10347957536557214720, 10347957537557214720] (0x8F9F2B4D20000000 - 0x8F9F2B4D40000000)
Covering bounds via _mbb: [-0.05, 47.95, 16.45, 48.45]
Exact predicate: Haversine distance <= 2500 meters
```

* **Engine** – which engine produced the plan and how many dimensions it operates on.
* **Index field** – the field scanned for `_idx` ranges.
* **Bounding box field** – the `_mbb` array used for coarse filtering.
* **Index ranges** – inclusive `_idx` ranges (decimal and hexadecimal) that will be probed.
* **Covering bounds** – the coarse bounding box applied prior to the exact predicate.
* **Exact predicate** – the human readable filter applied to candidates after the range scan.

## 3. Common checks

* **Range count** – plans that return a large number of ranges may benefit from higher precision or a larger `MaxCoveringCells` value.
* **Covering bounds** – for geographic queries crossing the anti-meridian you should see the bounds widen to the full world, indicating the engine split ranges internally.
* **Exact predicate** – verify the expected distance formula (`Haversine` or `Vincenty`) for geographic data or unit-based predicates for Cartesian data.

Use diagnostics during development to confirm index usage before rolling new precision or domain settings to production.
