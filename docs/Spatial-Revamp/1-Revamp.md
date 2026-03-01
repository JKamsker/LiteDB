# Epic: Modular Spatial Revamp (2D Geographic, 2D Cartesian, 3D Cartesian)

## Goals (scope)

* Extract an engine-agnostic core (`LiteDB.Spatial.Core`).
* Add engines:

  * `LiteDB.Spatial.Geographic` (2D, WGS84, meters).
  * `LiteDB.Spatial.Cartesian2D` (flat 2D).
  * `LiteDB.Spatial.Cartesian3D` (flat 3D points).
* Standardize internal fields:

  * `_idx` — numeric index key (Morton/Hilbert later).
  * `_mbb` — bounding box (4 or 6 values).
* Keep LINQ-friendly API with index-aware query plans.
* Provide backfill + deterministic metadata per collection.
* Ship diagnostics (“explain”) and a minimum GeoJSON I/O.
* Preserve backward compatibility as much as possible.

## Non-Goals (for now)

* 3D meshes/polyhedra predicates.
* Advanced projections/CRS transforms beyond WGS84.
* Alternate encoders beyond Morton (scaffold only).

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

# Stories & Acceptance Criteria

## S1 — Core contracts & geometry (engine-agnostic)

**Tasks**

* Add `GeoPoint`, `GeoPoint3D`, `BoundingBox` (2D: 4 elems; 3D: 6 elems).
* Define interfaces: `ISpatialEngine`, `ISpatialIndexEncoder`, `ISpatialDistance`, `ISpatialMapper`, `ISpatialQueryPlan`.
* Core options: `SpatialIndexOptions` (precision, max cells, tolerance, field names).
* LINQ surface: `SpatialExpressions` (Near/InBox overloads for 2D/3D).
* Stub `SpatialResolver` for expression translation.

**Acceptance**

* Build succeeds; no engine references.
* `BoundingBox` can represent 2D or 3D; `Dimensions` computed from length.
* Public API XML docs summarize semantics clearly.

**Definition of Done**

* Unit tests: struct immutability, simple ctor/property checks, box dimension tests.

---

## S2 — Indexing: Morton encoder (2D/3D)

**Tasks**

* Implement `MortonIndexEncoder(int dimensions)` with bit-interleaving:

  * Normalize/quantize doubles to unsigned ints (based on `PrecisionBits`).
  * Encode 2D and 3D variants to `ulong`.
* Provide helper to union adjacent ranges for nicer covers (range coalescing).

**Acceptance**

* Encoding deterministic across runs and platforms for same inputs.
* Round-trip tests on grid samples demonstrate locality (monotonic-ish order).

**Definition of Done**

* Unit tests: encode 2D/3D grids, boundary normalization (min/max), NaN handling (reject or sanitize).

---

## S3 — Metadata store & schema conventions

**Tasks**

* `SpatialMetadataStore` persists per-collection descriptor `{ engine, dimensions, options }` in `"_spatial_meta"`.
* Internal field names are customizable via options but default to `_idx` and `_mbb`.
* Guard: detect mismatch (e.g., engine=3D but `_mbb` length=4) and throw a friendly error.

**Acceptance**

* Persist + reload descriptor; equality/round-trip passes.
* Backward-compat hooks: if metadata missing, error suggests `UseGeographic(...)` (or chosen engine).

**Definition of Done**

* Unit tests for CRUD of metadata on an in-memory DB.

---

## S4 — Backfill utility

**Tasks**

* `SpatialBackfill.Run<T>(collection, descriptor, batchSize)`:

  * Iterates documents, computes `_idx`/`_mbb` via engine mapper, idempotent.
  * Counters: updated/skipped/errors; optional checkpoint key for large collections.

**Acceptance**

* Existing docs without fields are enriched; re-runs are no-ops.
* Partial failures report errors without corrupting others.

**Definition of Done**

* Integration test: seed docs → run backfill → rerun → counts make sense.

---

## S5 — Geographic engine (2D)

**Tasks**

* `GeographicEngine : ISpatialEngine` with:

  * `PlanNear2D` (meters; Haversine/Vincenty option).
  * `PlanWithinBox2D` (anti-meridian aware).
  * Mapper computing `_idx` (Morton2D) and `_mbb` (4-tuple) for points.
* Wrap/polar helpers.
* Public facade `SpatialGeographic`:

  * `EnsurePointIndex`, `Near`, `WithinBoundingBox`.

**Acceptance**

* Circle query transformed to index ranges + `_mbb` prefilter + exact predicate.
* Anti-meridian tests pass (e.g., boxes spanning ±180°).
* Distance parity sanity checks vs known pairs.

**Definition of Done**

* Tests: Berlin vicinity, longitude wrap, polar bounding boxes, error messages when engine/index missing.

---

## S6 — Cartesian 2D engine

**Tasks**

* `Cartesian2DEngine : ISpatialEngine`:

  * `PlanNear2D` (Euclidean in user units).
  * `PlanWithinBox2D`.
  * Mapper for `_idx` (Morton2D) and `_mbb`.
* Facade `SpatialCartesian2D`.

**Acceptance**

* Near/box queries prune using index and then exact Euclidean.
* No Earth-specific logic (no wrap/polar).

**Definition of Done**

* Tests on synthetic grid confirm index hit and precise filtering.

---

## S7 — Cartesian 3D engine (points)

**Tasks**

* `Cartesian3DEngine : ISpatialEngine`:

  * `PlanNear3D` (sphere → AABB → ranges → exact Euclidean3D).
  * `PlanWithinBox3D` (AABB).
  * Mapper for `_idx` (Morton3D) and `_mbb` (6-tuple).
* Facade `SpatialCartesian3D`.

**Acceptance**

* Near3D selects points within Euclidean radius; AABB prefilter reduces candidates.
* Works alongside 2D engines in same process.

**Definition of Done**

* Integration tests: mixed collections, precision bits impact, long/thin AABB range counts bounded by `MaxCoveringCells`.

---

## S8 — LINQ resolver

**Tasks**

* Implement `SpatialResolver` translation rules:

  * Recognize `SpatialExpressions.Near/InBox` overloads (2D/3D).
  * Read metadata; pick engine; request `ISpatialQueryPlan`.
  * Compose resulting `BsonExpression`s into `ILiteQueryable<T>`.
* Error messages: missing engine/index → actionable hints.

**Acceptance**

* LINQ usage remains index-aware; no full scans when index present.
* Mixed overloads route to correct engine.

**Definition of Done**

* Tests: LINQ queries produce plans that include index ranges; explain (S9) displays the same.

---

## S9 — Diagnostics (“Explain”)

**Tasks**

* `SpatialExplainResult` with engine, dims, ranges, predicates.
* `SpatialDiagnostics.Explain(plan)` returns a printable summary.

**Acceptance**

* For each engine, explain shows non-empty ranges and predicates matching the query.
* Helpful for bug reports (copy-pasteable string form).

**Definition of Done**

* Unit test snapshots for typical queries.

---

## S10 — GeoJSON I/O (minimal)

**Tasks**

* `GeoJsonSerializer` for `GeoPoint` (and basic Polygon/LineString stubs).
* Round-trip to/from `BsonValue`.

**Acceptance**

* Point round-trip stable; invalid input errors are clear.

**Definition of Done**

* Unit tests: valid/invalid shapes.

---

## S11 — Top-level facade & dispatch

**Tasks**

* `LiteDB.Spatial.Spatial`:

  * `UseGeographic/UseCartesian2D/UseCartesian3D` (persist metadata).
  * Engine-neutral `EnsurePointIndex`, `Near`, `WithinBoundingBox` (detect 2D/3D by accessor and box length; dispatch).
* Ensure creation of B-Tree on `"$._idx"` when ensuring index.

**Acceptance**

* Single, simple entry point for app code.
* Descriptive exceptions when a mismatch is detected.

**Definition of Done**

* Integration tests: full green-path for all three engines via top-level facade.

---

## S12 — Migration & docs

**Tasks**

* `docs/spatial-upgrade.md`: explain `_idx`/`_mbb`, metadata, backfill.
* `docs/spatial-guide.md`: usage per engine, examples, caveats.
* `docs/spatial-diagnostics.md`: how to read explain output.

**Acceptance**

* Docs compile to readable markdown; examples build/run in samples.

**Definition of Done**

* Sample app for Geographic 2D and Cartesian 3D.

---

## S13 — Benchmarks & regression guardrails

**Tasks**

* Micro-bench for `Near`/`InBox` with/without index; report candidate reductions.
* Baseline memory/CPU for encoding and planner.

**Acceptance**

* With index, candidate set shrinks substantially vs full scan on realistic densities.

**Definition of Done**

* Benchmarks run locally; numbers recorded in a markdown table for future comparison.

---

# Backward Compatibility & Risk Mitigation

* **Internal field rename**: standardize on `_idx` (previously `_gh`). Keep `_gh` reading optional if present (migration path), but **do not** write new `_gh`. Provide backfill to `_idx`.
* **Engine selection**: no global singletons; bind per collection via metadata to avoid cross-collection leakage.
* **Precision in 3D**: document that 3D needs higher `PrecisionBits` for similar spatial locality; default +4 vs 2D.
* **Range explosion**: cap by `MaxCoveringCells`; if exceeded, coarsen ranges and rely on exact filters (documented behavior).

---

# Public API sketch (stable surface)

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

# Test Matrix (minimum)

* **Core**: BoundingBox dims, options defaults, metadata round-trip.
* **Indexing**: Morton 2D/3D grids + boundaries.
* **Geographic**: Near Berlin; anti-meridian bbox; polar bbox.
* **Cartesian2D**: Near/InBox on grid; candidate pruning verified.
* **Cartesian3D**: Near3D + AABB on 3D lattice; MaxCoveringCells respected.
* **LINQ**: Expression translation picks correct engine; index predicate present.
* **Backfill**: idempotency; mixed docs with/without fields.
* **Diagnostics**: explain outputs consistent and human-readable.

---

# Deliverables checklist (per PR)

* [ ] Code compiles, no public API breaking changes outside the new surface.
* [ ] Unit + integration tests green.
* [ ] Samples updated.
* [ ] Docs updated (guide/upgrade/diagnostics).
* [ ] Bench baseline captured.
* [ ] Changelog entry with migration notes (`_gh` → `_idx`).

---

# Rollout plan

1. Merge Core + Indexing + Geographic (smallest behavior delta).
2. Introduce Cartesian2D (flat replacement for non-Earth use).
3. Add Cartesian3D (points + AABB + Near3D).
4. Enable LINQ resolver by default; keep a feature flag to disable if needed.
5. Publish docs and a sample upgrade script demonstrating backfill.