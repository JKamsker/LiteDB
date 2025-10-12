# Spatial Diagnostics

`LiteDB.Spatial.Core` ships with `SpatialDiagnostics` and `SpatialExplainResult` to make query plans observable. This document explains the structure of the explain output and how to interpret it when tuning indexes and tolerances.

## 1. Generating an explain result

Every engine returns an `ISpatialQueryPlan` when you call the planning helpers. Pass that plan to `SpatialDiagnostics.Explain` alongside the descriptor used to build it:

```csharp
var store = new SpatialMetadataStore(db);
var descriptor = store.GetRequiredDescriptor("places");

var plan = descriptor.EngineName switch
{
    GeographicEngine.EngineNameValue => SpatialGeographic.Near(descriptor, new GeoPoint(16.3738, 48.2082), 5_000),
    Cartesian2DEngine.EngineNameValue => SpatialCartesian2D.Near(descriptor, new GeoPoint(0, 0), 100),
    Cartesian3DEngine.EngineNameValue => SpatialCartesian3D.Near(descriptor, new GeoPoint3D(0, 0, 0), 100),
    _ => throw new InvalidOperationException()
};

var explain = SpatialDiagnostics.Explain(plan, descriptor);
Console.WriteLine(explain);
```

## 2. Anatomy of the output

A typical explain string looks like this:

```
Engine: Geographic2D (2D)
Index field: _idx
Bounding box field: _mbb
Index ranges (3) via _idx
  - [223372036854775808, 223372036854777855] (0x031C71C71C71C800 - 0x031C71C71C71CF1F)
  - [223372036856873984, 223372036857029631] (0x031C71C74F032000 - 0x031C71C75200007F)
  - [223372036860939264, 223372036861421567] (0x031C71C7B6664000 - 0x031C71C7BF3FFFFF)
Covering bounds via _mbb: [16.1738, 47.9082, 16.5738, 48.5082]
Covering cells: 96 (fallback)
Exact predicate: Vincenty distance <= 5000 meters
```

* **Engine** – the engine that produced the plan.
* **Index field** – the field used for Morton range scans.
* **Bounding box field** – the field used for coarse filtering (`null` when the plan does not require one).
* **Index ranges** – the Morton windows that will be scanned. Values are displayed as decimal and hexadecimal for easy comparison.
* **Covering bounds** – the bounding box applied before exact predicates.
* **Covering cells** – the number of Morton ranges requested before enforcing `MaxCoveringCells`. `fallback` indicates the encoder merged ranges because the limit was exceeded.
* **Exact predicate** – the final check executed for each candidate.

## 3. Bounding boxes and anti-meridian ranges

For geographic plans the covering bounds always express world coordinates. When a query spans the anti-meridian, `SpatialGeographic` emits multiple ranges and widens the covering bounds to `[-180, maxLat]..[180, minLat]`. The explain output surfaces the aggregated bounds so you can confirm the north/south limits are correct.

Example:

```
Covering bounds via _mbb: [170, -10, 190, 10]
```

Even though `190` exceeds 180°, the stored `_mbb` arrays keep the raw values, so comparing with document values remains straightforward.

## 4. Cartesian diagnostics

Cartesian plans report the domain-normalised ranges and the exact predicate used for Euclidean checks:

```
Engine: Cartesian3D (3D)
Index ranges (1) via _idx
  - [4294967296, 4380866642] (0x0000000100000000 - 0x0000000105341202)
Covering bounds via _mbb: [-10, -10, -10, 10, 10, 10]
Covering cells: 1
Exact predicate: Euclidean3D <= 25
```

Use the covering bounds to verify that the query stays within your configured domain.

## 5. Inspecting `_spatial_meta`

`SpatialDiagnostics` does not modify metadata. If you want to ensure descriptors are kept up to date, query `_spatial_meta` directly:

```csharp
var metaCollection = db.GetCollection("_spatial_meta");
var descriptors = metaCollection.FindAll().ToList();
```

Each document includes the engine name, dimensionality, geometry field, options, and engine-specific settings (e.g., distance mode or Cartesian domain).

## 6. Troubleshooting tips

* **Empty `Index ranges`** – the planner could not create a Morton covering. Check that the radius or bounding box produces a valid shape and that `MaxCoveringCells` is not overly restrictive.
* **Unexpected engine** – confirm you configured the collection with the correct `Use*` helper. The explain output reflects the engine stored in metadata.
* **No covering bounds** – some queries (e.g., very large radius searches) intentionally skip the `_mbb` prefilter. Exact predicates will still run.

## 7. Automating in tests

The explain API is lightweight and can be used in unit tests to guard against regressions:

```csharp
var plan = SpatialGeographic.Near(descriptor, center, 5_000);
var explain = SpatialDiagnostics.Explain(plan, descriptor);
explain.IndexRanges.Count.Should().BeLessThanOrEqualTo(descriptor.Options.MaxCoveringCells);
```

Combining explain assertions with query results helps catch both performance regressions and correctness issues early in CI.

## 8. Refreshing Cartesian3D fixtures

The differential tests under `LiteDB.Spatial.Core.Tests/Differential/Cartesian3D` rely on deterministic lattice fixtures. Regenerate them whenever you adjust the lattice spacing or query mix:

```
dotnet run --project scripts/Cartesian3DFixtures/Cartesian3DFixtures.csproj
```

The console app writes JSON fixtures into `LiteDB.Spatial.Core.Tests/Differential/Cartesian3D/Fixtures` without requiring external tooling. Commit the updated JSON alongside any test changes so CI and diagnostics stay in sync.

