# Epic: Modular Spatial Revamp (2D Geographic, 2D Cartesian, 3D Cartesian)

## Goals (scope)

- Extract an engine-agnostic core (`LiteDB.Spatial.Core`).
- Add engines:
  - `LiteDB.Spatial.Geographic` (2D, WGS84, meters).
  - `LiteDB.Spatial.Cartesian2D` (flat 2D).
  - `LiteDB.Spatial.Cartesian3D` (flat 3D points).
- Standardize internal fields:
  - `_idx` — numeric index key (Morton/Hilbert later).
  - `_mbb` — bounding box (4 or 6 values).
- Keep LINQ-friendly API with index-aware query plans.
- Provide backfill + deterministic metadata per collection.
- Ship diagnostics ("explain") and a minimum GeoJSON I/O.
- Preserve backward compatibility as much as possible.

## Non-Goals (for now)

- 3D meshes/polyhedra predicates.
- Advanced projections/CRS transforms beyond WGS84.
- Alternate encoders beyond Morton (scaffold only).

---

## Architecture overview (target)

```
LiteDB.Spatial.Core
  Geometry, Engine contracts, LINQ resolver, Metadata, Backfill

LiteDB.Spatial.Indexing
  MortonIndexEncoder (2D/3D), Hilbert (stub)

LiteDB.Spatial.Geographic
  GeographicEngine (2D), Haversine/Vincenty, wrap/polar helpers, facade

LiteDB.Spatial.Cartesian2D
  Cartesian2DEngine, Euclidean2D, facade

LiteDB.Spatial.Cartesian3D
  Cartesian3DEngine, Euclidean3D, facade

LiteDB.Spatial.Diagnostics
  Explain(plan) utilities

LiteDB.Spatial.GeoJson
  Basic GeoJSON serializer

LiteDB.Spatial
  Top-level facade (dispatch by collection metadata)
```

---

## Stories & Acceptance Criteria

### S1 — Core contracts & geometry (engine-agnostic)

- [x] Add `GeoPoint`, `GeoPoint3D`, `BoundingBox` (2D: 4 elems; 3D: 6 elems).
- [x] Define interfaces: `ISpatialEngine`, `ISpatialIndexEncoder`, `ISpatialDistance`, `ISpatialMapper`, `ISpatialQueryPlan`.
- [x] Core options: `SpatialIndexOptions` (precision, max cells, tolerance, field names).
- [x] LINQ surface: `SpatialExpressions` (Near/InBox overloads for 2D/3D).
- [x] Stub `SpatialResolver` for expression translation.

### S2 — Indexing: Morton encoder (2D/3D)

- [x] Implement `MortonIndexEncoder(int dimensions)` with bit-interleaving and helpers.
- [x] Provide helper to union adjacent ranges for nicer covers.

**Notes**

- Added a reusable `MortonIndexEncoder` that normalizes inputs, emits deterministic Morton codes for two and three dimensions, and can coalesce covering ranges when callers need a coarse approximation.
- Introduced unit tests that exercise 2D/3D encodings, NaN validation, covering enumeration, and range union behaviour.

### S3 — Metadata store & schema conventions

- [x] `SpatialMetadataStore` persists per-collection descriptor `{ engine, dimensions, options }` in `"_spatial_meta"`.
- [x] Internal field names customizable via options but default to `_idx` and `_mbb`.
- [x] Guard mismatches and surface friendly errors.

**Notes**

- Implemented `SpatialCollectionDescriptor` equality semantics, persisted geometry field metadata, and introduced `SpatialEngineSettings` so descriptors round-trip per-engine configuration like Cartesian domains and geographic distance modes.
- Added schema validation helpers to flag mismatched bounding box lengths and non-numeric index/bounding box values, easing diagnosis of dimensional errors.

### S4 — Backfill utility

- [x] `SpatialBackfill.Run<T>(collection, descriptor, batchSize)` populates `_idx`/`_mbb`.
- [x] Ensure idempotent behavior with detailed counters and error reporting.

**Notes**

- Delivered a document-oriented backfill runner that pages through collections, batches writes, captures the last checkpoint, and records processed/updated/skipped counts alongside exception details for failed documents.
- Added smoke tests with stub engines to confirm idempotency, checkpoint handling, and resilience to malformed documents.

### S5 — Geographic engine (2D)

- [x] Deliver `GeographicEngine` with near/box planners, mapper, and facade helpers.
  - `PlanNear2D` must expose both Haversine and Vincenty distance modes with range coalescing tuned for meter-based tolerances.
  - `PlanWithinBox2D` needs explicit anti-meridian handling so bounding boxes that cross ±180° still emit coherent `_mbb` filters.
  - Mapper should quantize lon/lat into Morton2D codes, normalize bounding boxes for the encoder, and persist world-space `_mbb` values for diagnostics.
- [x] Handle wraparound and polar helpers with tests for real-world coordinates.
  - Introduce helpers for longitude wrapping and polar clamping to avoid discontinuities around the poles.
  - Guard rails for invalid latitude/longitude inputs with actionable error messages surfaced through the facade.
- [x] Ship `SpatialGeographic` facade utilities (`EnsurePointIndex`, `Near`, `WithinBoundingBox`) delegating to the engine, wiring common options, and invoking backfill automatically.

**Testing outline**

- Regression tests for Berlin vicinity distances and tolerance envelopes vs GeographicLib samples.
- Anti-meridian fixtures (e.g., Aleutian Islands) verifying bounding box queries prune correctly.
- Polar bounding boxes proving wrap helpers maintain deterministic Morton codes.
- Facade integration tests ensuring friendly errors when metadata or indexes are missing.

### S6 — Cartesian 2D engine

- [x] Provide `Cartesian2DEngine` and facade with Euclidean planning and mapping.
  - `PlanNear2D` should compute Euclidean radii, fall back to bounding-box approximations when `MaxCoveringCells` is exceeded, and reuse Morton2D coverings.
  - `PlanWithinBox2D` maps axis-aligned bounding boxes directly to `_mbb` predicates while keeping coverings in the same coordinate space.
- [x] Implement mapper to encode points with Morton2D and persist `_mbb` spans without geographic-specific logic.
- [x] Ship `SpatialCartesian2D` facade mirroring the geographic API surface for flat-coordinate callers and triggering backfill on configuration.

**Testing outline**

- Synthetic grid suites confirming near/box queries leverage the index and return precise Euclidean distances.
- Property-based tests covering randomized bounding boxes to ensure deterministic Morton ordering and pruning.
- Facade smoke tests validating options propagation and descriptive errors when the index is absent.

### S7 — Cartesian 3D engine (points)

- [x] Implement `Cartesian3DEngine` and facade for point-based 3D queries.
  - `PlanNear3D` forms spherical shells via Morton3D coverings, adds `_mbb` pruning, and applies exact Euclidean3D distance filtering.
  - `PlanWithinBox3D` translates 3D axis-aligned bounding boxes into `_mbb` spans and Morton3D range plans.
- [x] Implement mapper to normalize XYZ coordinates, emit Morton3D keys, and maintain six-value `_mbb` arrays respecting descriptor precision.
- [x] Ship `SpatialCartesian3D` facade with helpers for ensuring indexes, running near queries, and bulk backfills for point clouds.

**Testing outline**

- 3D lattice fixtures validating near-query shells, range coalescing, and respect for `MaxCoveringCells` fallbacks.
- Randomized AABB suites checking mapper output and `_mbb` normalization for varied coordinate scales.
- Euclidean3D parity checks vs MathNet.Spatial (or similar oracle) to guarantee floating-point tolerance targets.

### S8 — LINQ resolver

- [x] Translate `SpatialExpressions` into query plans based on metadata.

**Notes**

- Implemented a metadata-aware `SpatialResolver` that recognises the new `SpatialExpressions` helpers, resolves the appropriate engine, and surfaces descriptive errors when configuration is missing or mismatched.

### S9 — Diagnostics ("Explain")

- [x] Produce printable `SpatialExplainResult` summaries for query plans.

**Notes**

- Added `SpatialDiagnostics.Explain` and `SpatialExplainResult` to capture engine, range, and predicate details with a readable multi-line summary for troubleshooting.

### S10 — GeoJSON I/O (minimal)

- [x] `GeoJsonSerializer` round-trips GeoPoint/Polygon/LineString data.

**Notes**

- Delivered a lightweight GeoJSON serializer capable of validating and round-tripping points, line strings, and polygons using the new geometry primitives.

### S11 — Top-level facade & dispatch

- [x] `LiteDB.Spatial.Spatial` entry point manages engine configuration and dispatch.

**Notes**

- Added a dedicated `LiteDB.Spatial` project that wires metadata persistence, index
  creation, backfill, and plan dispatch across the geographic, Cartesian 2D, and
  Cartesian 3D engines. The facade automatically provisions a B-Tree on `"$._idx"` and
  exposes engine-neutral helpers for `Use*`, `EnsurePointIndex`, `Near`, and
  `WithinBoundingBox`.
- Integration tests exercise the facade end-to-end for each engine and assert that plans
  are emitted with non-empty Morton ranges and compatible bounding boxes.

### S12 — Migration & docs

- [x] Author upgrade/guide/diagnostics documentation with samples.

**Notes**

- Authored `docs/spatial-upgrade.md`, `docs/spatial-guide.md`, and
  `docs/spatial-diagnostics.md` to describe the new metadata workflow, index backfill,
  manual plan execution, and explain output.
- Updated the minimal API sample to use the facade directly for both geographic and
  Cartesian 3D collections.

### S13 — Benchmarks & regression guardrails

- [x] Capture performance baselines and add regression guardrails.

**Notes**

- Reworked the spatial benchmark to compare near/bounding-box workloads with and
  without index-backed plan execution while capturing candidate reduction.
- Added an integration test that validates the facade-driven plan trims candidate sets on
  a dense Cartesian grid, providing a regression guardrail for planner changes.
- Standardized benchmark imports around the aliased `LiteDB` reference so the suite builds
  alongside the new facade project without manual edits per benchmark class.

---

## Backward Compatibility & Risk Mitigation

- **Internal field rename**: standardize on `_idx` (previously `_gh`). Keep `_gh` reading optional if present (migration path), but **do not** write new `_gh`. Provide backfill to `_idx`.
- **Engine selection**: no global singletons; bind per collection via metadata to avoid cross-collection leakage.
- **Precision in 3D**: document that 3D needs higher `PrecisionBits` for similar spatial locality; default +4 vs 2D.
- **Range explosion**: cap by `MaxCoveringCells`; if exceeded, coarsen ranges and rely on exact filters (documented behavior).

---

## Public API sketch (stable surface)

```csharp
// Top-level
Spatial.UseGeographic(col, new GeographicOptions { PrecisionBits = 32 });
Spatial.EnsurePointIndex(col, x => x.Location); // GeoPoint
var near = Spatial.Near(col, x => x.Location, new GeoPoint(lon, lat), radius: 1500).ToList();

Spatial.UseCartesian2D(col2, new SpatialIndexOptions { PrecisionBits = 28 });
Spatial.EnsurePointIndex(col2, x => x.Pos2D); // GeoPoint
var inBox = Spatial.WithinBoundingBox(col2, x => x.Pos2D, BoundingBox.From2D(0,0,10,10)).ToList();

Spatial.UseCartesian3D(col3, new SpatialIndexOptions { PrecisionBits = 28 });
Spatial.EnsurePointIndex(col3, x => x.Pos3D); // GeoPoint3D
var hits = Spatial.Near(col3, x => x.Pos3D, new GeoPoint3D(1,2,3), radius: 5).ToList();
```

---

## Test Matrix (minimum)

- **Core**: BoundingBox dims, options defaults, metadata round-trip.
- **Indexing**: Morton 2D/3D grids + boundaries.
- **Geographic**: Near Berlin; anti-meridian bbox; polar bbox.
- **Cartesian2D**: Near/InBox on grid; candidate pruning verified.
- **Cartesian3D**: Near3D + AABB on 3D lattice; MaxCoveringCells respected.
- **LINQ**: Expression translation picks correct engine; index predicate present.
- **Backfill**: idempotency; mixed docs with/without fields.
- **Diagnostics**: explain outputs consistent and human-readable.

---

## Deliverables checklist (per PR)

- [ ] Code compiles, no public API breaking changes outside the new surface.
- [ ] Unit + integration tests green.
- [ ] Samples updated.
- [ ] Docs updated (guide/upgrade/diagnostics).
- [ ] Bench baseline captured.
- [ ] Changelog entry with migration notes (`_gh` → `_idx`).

---

## Rollout plan

1. Merge Core + Indexing + Geographic (smallest behavior delta).
2. Introduce Cartesian2D (flat replacement for non-Earth use).
3. Add Cartesian3D (points + AABB + Near3D).
4. Enable LINQ resolver by default; keep a feature flag to disable if needed.
5. Publish docs and a sample upgrade script demonstrating backfill.

---

## Addendum: Battle-Tested Oracles & Datasets

### Reference libraries (use as **oracles**, not dependencies of runtime)

- **Geometry predicates (2D):** `NetTopologySuite (NTS)` — robust predicates: `Contains`, `Intersects`, `Within`, polygon holes/edge cases.
- **Geodesic distances (Earth):** `GeographicLib` — great-circle, inverse geodesic; use as golden values.
- **Projections / CRS transforms (optional sanity):** `ProjNET` — to cross-check lon/lat ↔ projected calculations for small-radius approximations.
- **3D math:** `MathNet.Spatial` — Euclidean 3D distances and vector ops for oracle checks.
- **GeoJSON parsing (test-only):** `GeoJSON.Net` — load complex fixtures without maintaining your own parser.
- **External systems for differential tests (CI optional):** `PostGIS`, `SQLite+RTree/SpatiaLite` for bbox/near behavior.

### Public datasets & fixtures

- `Turf.js` fixtures (GeoJSON): points/lines/polygons with holes; great for predicate parity.
- `Natural Earth`: coastlines, large polygons crossing the anti-meridian (Aleutians, Russia); polar stress.
- `OSM` extracts (tiny tiles): dense urban point clouds for performance & correctness mixes.
- Synthetic grids & lattices: controlled point sets for Morton/Hilbert locality tests.
- Precomputed geodesic pairs from GeographicLib samples (save as JSON with expected distances).

### New Stories (append to Epic)

#### T1 — Oracle Adapters

- [ ] Add `LiteDB.Spatial.Testing.Oracles` project with adapters for NTS, GeographicLib, MathNet, PostGIS.

#### T2 — Golden Fixtures & Tolerances

- [ ] Store fixtures under `/tests/fixtures` with documented tolerances and snapshot helpers.

#### T3 — Differential Correctness: Geographic 2D

- [ ] Cross-check `SpatialGeographic` behavior against PostGIS and GeographicLib oracles.

#### T4 — Differential Correctness: Cartesian 2D

- [ ] Validate Euclidean behavior with NTS and property-based testing.

#### T5 — Differential Correctness: Cartesian 3D

- [ ] Ensure 3D results align with MathNet.Spatial and range caps.

#### T6 — Index Locality & Stability (Morton)

- [ ] Measure locality/monotonicity and compare to reference encoders.

#### T7 — Query Plan Parity (LINQ)

- [ ] Assert LINQ plans use indexes and match oracles.

#### T8 — Performance Sanity vs External Systems (optional `perf`)

- [ ] Benchmark LiteDB.Spatial vs SQLite/PostGIS and record findings.

---

## Concrete snippets (test-only patterns)

```csharp
// Distance parity (Geographic)
var d1 = GeographicLibOracle.Distance(lon1, lat1, lon2, lat2);
var d2 = SpatialGeographicDistance.Haversine(lon1, lat1, lon2, lat2);
d2.Should().BeApproximately(d1, 1e-4 * d1 + 0.05);
```

```csharp
// Predicate parity (2D)
var expected = NtsOracle.Contains(poly, pt);
var actual = SpatialExpressions.Within(/* polygon */, /* point */);
actual.Should().Be(expected);
```

```csharp
// 3D near parity
var dRef = MathNetOracle3D.Distance(a, b);
var dOur = Euclidean3D.Distance(a, b);
dOur.Should().BeApproximately(dRef, 1e-9);
```

---

## Integration guidance

- Keep external libraries under `tests` only and guard with categories/env vars.
- Persist expensive oracle outputs to JSON fixtures for reproducible CI runs.
- Document toggles like `SPATIAL_ORACLES` and `SPATIAL_DB_TESTS` for optional suites.

---

## Acceptance Criteria (global)

- Geographic distances match GeographicLib within tolerance on 1,000 diverse pairs.
- 2D predicates match NTS on 100 complex polygons × 1,000 random points each.
- 3D distances match MathNet.Spatial within floating-point tolerance on 10,000 random pairs.
- Index usage verified by explain output in every LINQ test; no full scans when index present.
- Fixtures pack checked into the repo; tests avoid network calls.

