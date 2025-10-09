# Spatial Diagnostics

`LiteDB.Spatial.Core` ships a diagnostic surface that makes it easy to inspect index
usage and predicates produced by the planner. Diagnostics are built around two types:

- `ISpatialQueryPlan` — emitted by the engines and the LINQ resolver.
- `SpatialExplainResult` — a formatted view that highlights index ranges and filters.

## Capturing a plan

Plans originate from calls to `Spatial.Near`, `Spatial.WithinBoundingBox`, or LINQ
expressions that use `SpatialExpressions`. The example below issues a geographic near
query, then renders the explain output.

```csharp
extern alias LiteDbBase;

using LiteDB.Spatial;
using BaseLiteDB = LiteDbBase::LiteDB;

using var db = new BaseLiteDB.LiteDatabase("Filename=geo.db");
var metadata = new SpatialMetadataStore(db);
var places = db.GetCollection("places");

Spatial.UseGeographic(metadata, places.Name, "location");
var descriptor = Spatial.EnsurePointIndex(metadata, places);

var plan = Spatial.Near(descriptor, new GeoPoint(16.37, 48.21), radius: 5_000);
var explain = SpatialDiagnostics.Explain(plan, descriptor);
Console.WriteLine(explain);
```

## Sample output

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

### Interpreting the output

- **Engine / Dimensions**: Confirms which engine produced the plan.
- **Index field**: The document field scanned by the B-Tree (`_idx` by default).
- **Bounding box field**: The coarse bounding box persisted alongside index values.
- **Index ranges**: Closed Morton ranges expressed in decimal and hexadecimal form.
- **Covering bounds**: The union of `_mbb` values used for pre-filtering.
- **Exact predicate**: Human-readable description of the final distance or containment
  check applied after index pruning.

A healthy plan contains one or more ranges and a non-`none` exact predicate. Missing
ranges typically indicate that the collection was not configured with `Spatial.Use*`
or that `Spatial.EnsurePointIndex` has not been run.

## Logging recommendations

- Persist the explain output alongside request IDs to simplify triage.
- Track `RangeCount` (`SpatialExplainResult.RangeCount`) over time to detect
  regressions in Morton coverings.
- Use the diagnostics in integration tests to assert that queries continue to rely on
  `_idx` and `_mbb` even as the LINQ surface evolves.
