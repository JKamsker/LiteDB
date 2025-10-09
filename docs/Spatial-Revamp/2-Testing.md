# Addendum: Battle-Tested Oracles & Datasets

## Reference libraries (use as **oracles**, not dependencies of runtime)

* **Geometry predicates (2D):**

  * **NetTopologySuite (NTS)** — robust predicates: `Contains`, `Intersects`, `Within`, polygon holes/edge cases.
* **Geodesic distances (Earth):**

  * **GeographicLib** (C# port if available; otherwise precomputed vectors from C++/CLI) — great-circle, inverse geodesic; use as golden values.
* **Projections / CRS transforms (optional sanity):**

  * **ProjNET** — to cross-check lon/lat ↔ projected calculations for small-radius approximations.
* **3D math:**

  * **MathNet.Spatial** — Euclidean 3D distances and vector ops for oracle checks.
* **GeoJSON parsing (test-only):**

  * **GeoJSON.Net** (Newtonsoft) — load complex fixtures without maintaining your own parser.
* **External systems for differential tests (CI optional):**

  * **PostGIS** (Docker) — `ST_DWithin`, `ST_Intersects`, anti-meridian polygons;
  * **SQLite+RTree/SpatiaLite** — bbox/near behavior.

## Public datasets & fixtures

* **Turf.js fixtures** (GeoJSON): points/lines/polygons with holes; great for predicate parity.
* **Natural Earth**: coastlines, large polygons crossing the anti-meridian (Aleutians, Russia); polar stress.
* **OSM extracts (tiny tiles)**: dense urban point clouds for performance & correctness mixes.
* **Synthetic grids & lattices**: controlled point sets for Morton/Hilbert locality tests.
* **Precomputed geodesic pairs** from GeographicLib samples (save as JSON with expected distances).

---

# New Stories (append to Epic)

## T1 — Oracle Adapters

**Tasks**

* Add `LiteDB.Spatial.Testing.Oracles` project with thin adapters:

  * `NtsOracle`: `Contains`, `Intersects`, `Within` from GeoJSON/arrays.
  * `GeographicLibOracle`: `Distance(lon1,lat1,lon2,lat2)`; load golden vectors.
  * `MathNetOracle3D`: `Distance3D(x1,y1,z1,x2,y2,z2)`.
  * `PostgisOracle` (optional, `Docker`-guarded): run SQL and return result sets.

**Acceptance**

* Each oracle behind an interface; can be skipped if env var `SPATIAL_ORACLES=off`.

---

## T2 — Golden Fixtures & Tolerances

**Tasks**

* Store fixtures under `/tests/fixtures`:

  * `geodesic_pairs.json` (from GeographicLib; meters).
  * `geojson_polygons/*.json` (holes, self-touching, anti-meridian).
  * `point_clouds/*.json` (dense 2D & 3D lattices).
* Define numeric tolerances:

  * Distances: **Earth** `≤ 1e-4 * distance + 0.05 m` (Vincenty/Haversine parity),
    **2D/3D** `≤ 1e-9 * scale + 1e-9`.
* Add snapshot helpers: store failing pairs for triage.

**Acceptance**

* Tests read fixtures without internet; failures print input IDs + deltas.

---

## T3 — Differential Correctness: Geographic 2D

**Tasks**

* For `SpatialGeographic.Near`:

  * Compare results vs **PostGIS `ST_DWithin`** on the same points (radius in meters).
  * Cross-check pairwise distances vs **GeographicLib**.
* For `WithinBoundingBox` across anti-meridian:

  * Validate with **NTS** after splitting box at ±180° (oracle method).
* Edge cases: poles (|lat| ≥ 85°), boxes that straddle ±180°, tiny radii (≤ 1 m).

**Acceptance**

* Result sets equal to oracle (allow ordering differences).
* Distance deltas within tolerance; anti-meridian parity proven on Natural Earth shapes.

---

## T4 — Differential Correctness: Cartesian 2D

**Tasks**

* `Near` and `InBox`:

  * Compare vs **NTS** for predicates on the same synthetic datasets.
  * Validate Euclidean distances vs closed-form oracle.
* Property-based tests with **FsCheck**:

  * Random convex polygons + random points → `Contains` parity with NTS.
  * Random AABBs → `InBox` ⇔ coordinate inequalities.

**Acceptance**

* 1,000 random cases per run; zero mismatches or reproducible counterexamples saved to `/tests/failures`.

---

## T5 — Differential Correctness: Cartesian 3D

**Tasks**

* `Near3D`:

  * Compare distances vs **MathNet.Spatial** oracle.
  * AABB queries parity with manual inequalities.
* Grids & long/thin AABBs to pressure-test **range cover explosion**; ensure fallback to coarser covers obeys results correctness.
* Capture covering metrics (cell counts, range counts, fallback flags) so logs highlight when `MaxCoveringCells` triggers.

**Acceptance**

* Exact match on membership; distance deltas within tolerance.
* Log “cover cells used” ≤ `MaxCoveringCells`; if capped, correctness still holds.
* Fixtures under `tests/fixtures/cartesian3d` regenerate via `dotnet run --project scripts/Cartesian3DFixtureGenerator` without external downloads.

---

## T6 — Index Locality & Stability (Morton)

**Tasks**

* Verify **locality**: for synthetic grids, nearest neighbors in space have **high probability** of close `_idx` (statistic, not strict).
* Verify **monotonicity in 1D slices**: fix Y (and Z) → `_idx` increases with X.
* Cross-check against an independent **Morton encoder** (e.g., a tiny C++ tool or a known implementation output saved to fixtures).

**Acceptance**

* Locality metric above threshold (e.g., top-k neighbor overlap ≥ 90% for uniform grids).
* 1D slice monotonicity holds exactly.
* Bit-exact match to reference encoder for sample points.

---

## T7 — Query Plan Parity (LINQ)

**Tasks**

* For each LINQ query using `SpatialExpressions`, assert:

  * **Plan contains an index predicate** on `"$._idx"`.
  * **MBB prefilter** present when applicable.
  * **Exact predicate** present and last in evaluation order (explain).
* Compare query results vs oracles (NTS/GeographicLib/MathNet).

**Acceptance**

* No test path falls back to full scan when index is present (inspect explain).

---

## T8 — Performance Sanity vs External Systems (optional CI tag `perf`)

**Tasks**

* Benchmark `Near`/`InBox` on:

  * LiteDB.Spatial (with `_idx`),
  * **SQLite+RTree**,
  * **PostGIS** (coarse comparison).
* Report candidate reductions (index → prefilter → exact) and wall time.

**Acceptance**

* LiteDB hits show meaningful candidate shrinkage; numbers recorded to `/docs/benchmarks.md`.

---

# Concrete snippets (test-only patterns)

### Distance parity (Geographic)

```csharp
// Arrange
var pairs = Load<GeoPair[]>("fixtures/geodesic_pairs.json");
foreach (var p in pairs)
{
    var d1 = GeographicLibOracle.Distance(p.Lon1, p.Lat1, p.Lon2, p.Lat2);
    var d2 = SpatialGeographicDistance.Haversine(p.Lon1, p.Lat1, p.Lon2, p.Lat2);
    var tol = 1e-4 * d1 + 0.05; // meters
    d2.Should().BeApproximately(d1, tol);
}
```

### Predicate parity (2D)

```csharp
var poly = NtsOracle.LoadPolygon("fixtures/geojson_polygons/hole.json");
foreach (var pt in RandomPointsInExtent(poly.EnvelopeInternal, 500))
{
    var expected = NtsOracle.Contains(poly, pt);
    var actual = SpatialExpressions.Within(/* our polygon */, /* our point */); // via adapter
    actual.Should().Be(expected);
}
```

### 3D near parity

```csharp
var a = new GeoPoint3D(1,2,3);
var b = new GeoPoint3D(1.1, 2.1, 3.1);
var dRef = MathNetOracle3D.Distance(a,b);
var dOur  = Euclidean3D.Distance(a,b);
dOur.Should().BeApproximately(dRef, 1e-9);
```

---

# How to integrate without polluting production

* Place all external libs under `tests` projects only.
* Introduce an **`[Category("oracle")]`** attribute; allow `dotnet test -l "console;verbosity=normal" --filter TestCategory=oracle` to toggle.
* For PostGIS/SQLite tests, guard with `EnvVar("SPATIAL_DB_TESTS") == "1"`.
* Persist expensive oracle outputs to JSON **fixtures** and run parity against those in regular CI to avoid Docker flakiness.

---

# Acceptance Criteria (global, add to Epic)

* All **Geographic distances** match **GeographicLib** within the specified tolerance on at least **1,000** diverse pairs (short/long, polar, anti-meridian).
* All **2D predicates** match **NTS** on:

  * ≥ 100 complex polygons (holes, self-touching) × 1,000 random points each.
* **3D distances** match **MathNet.Spatial** exactly within floating-point tolerance across ≥ 10,000 random pairs.
* **Index usage** is verified by explain output in every LINQ test; no silent full scans.
* A reproducible **fixtures pack** lives in repo; tests never depend on the network.