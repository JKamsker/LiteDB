# Pretext
This is an aggregation of the findings from 
- docs/Spatial-Revamp/Testing-Report-Differential-Cartesian3D.md
- docs/Spatial-Revamp/Testing-Report-FsCheck-Property-Tests.md
- docs/Spatial-Revamp/Testing-Report-LiteDB.Spatial-Testing-Oracles.md
- docs/Spatial-Revamp/Testing-Report-Spatial-Test-Suites-and-Features.md
- docs/Spatial-Revamp/Testing-Report-Synthetic-Grid-Locality.md
rephrased into a task.

Set ``WORKTREES_ROOT=/home/jonas/work/LiteDB.wt/worktrees`` (``export WORKTREES_ROOT=/home/jonas/work/LiteDB.wt/worktrees``) – each archived PR lives there as its own worktree. Quick inventory:

```
jonas@ubuntu-server:~/work/LiteDB.wt/feat-spatial-revamp$ ls $WORKTREES_ROOT
pr-73-codex-add-litedb.spatial.testing.oracles-project         pr-80-codex-add-spatial-test-suites-and-features-90ok4a           pr-87-codex-add-tests-for-differential-cartesian3d-xchzvh
pr-74-codex-add-litedb.spatial.testing.oracles-project-tyis6a  pr-81-codex-add-fscheck-property-tests-for-litedb.spatial         pr-88-codex-add-tests-for-differential-cartesian3d-hzb3dw
pr-75-codex-add-litedb.spatial.testing.oracles-project-3mxi4x  pr-82-codex-add-fscheck-property-tests-for-litedb.spatial-w1yac6  pr-89-codex-add-tests-for-differential-cartesian3d-mu1co1
pr-76-codex-add-litedb.spatial.testing.oracles-project-q6eito  pr-83-codex-add-fscheck-property-tests-for-litedb.spatial-81ut5t  pr-90-codex-implement-synthetic-grid-tests-for-locality
pr-77-codex-add-spatial-test-suites-and-features               pr-84-codex-add-fscheck-property-tests-for-litedb.spatial-m14epp  pr-91-codex-implement-synthetic-grid-tests-for-locality-0l0524
pr-78-codex-add-spatial-test-suites-and-features-xfbzfd        pr-85-codex-add-tests-for-differential-cartesian3d                pr-92-codex-implement-synthetic-grid-tests-for-locality-6kdpc9
pr-79-codex-add-spatial-test-suites-and-features-9k1ruu        pr-86-codex-add-tests-for-differential-cartesian3d-t0phsb         pr-93-codex-implement-synthetic-grid-tests-for-locality-ae302w
```

Leverage those commits directly (remote refs no longer exist):

```
git -C "$WORKTREES_ROOT/pr-87-codex-add-tests-for-differential-cartesian3d-xchzvh" rev-parse HEAD
git -C "$WORKTREES_ROOT/pr-93-codex-implement-synthetic-grid-tests-for-locality-ae302w" rev-parse HEAD
# …see full mapping with:
for dir in $WORKTREES_ROOT/pr-*; do printf '%-70s %s\n' "$(basename "$dir")" "$(git -C "$dir" rev-parse HEAD)"; done
```

# One Task: aggregate + merge plan

## 0) Create the working branch

```bash
git switch -c feat/spatial-revamp-testing feat/spatial-revamp
```

---

## 1) Spatial **Testing Oracles** — base on `pr-76`, layer infra from `pr-73`

* Rationale: `pr-76` is the only branch that already wires adapters into real parity tests; `pr-73` has the best environment toggles, per-oracle gating, fixture infra, and tolerance helpers. Drop `pr-74`/`pr-75`. 

```bash
# 1A. Merge the working baseline from pr-76 (adapters + parity tests)
git merge --no-ff $(git -C "$WORKTREES_ROOT/pr-76-codex-add-litedb.spatial.testing.oracles-project-q6eito" rev-parse HEAD)

# 1B. Bring per-oracle env gating, skip attribute, and infra from pr-73
git checkout $(git -C "$WORKTREES_ROOT/pr-73-codex-add-litedb.spatial.testing.oracles-project" rev-parse HEAD) -- \
  LiteDB.Spatial.Testing.Oracles/OracleEnvironment.cs \
  LiteDB.Spatial.Testing.Oracles/OracleSkipAttribute.cs \
  LiteDB.Spatial.Testing.Oracles/SpatialPrimitives.cs \
  LiteDB.Spatial.Testing.Oracles/GeographicLibOracle.cs \
  LiteDB.Spatial.Testing.Oracles/MathNetOracle3D.cs \
  LiteDB.Spatial.Testing.Oracles/NtsOracle.cs \
  LiteDB.Spatial.Testing.Oracles/PostgisOracle.cs \
  LiteDB.Spatial.Core.Tests/Infrastructure/FixtureLoader.cs \
  LiteDB.Spatial.Core.Tests/Infrastructure/SpatialTolerance.cs \
  LiteDB.Spatial.Core.Tests/Infrastructure/FailureSnapshot.cs \
  tests/fixtures/geojson_polygons/ \
  tests/fixtures/geodesic_pairs.json

# 1C. Ensure the oracle project targets net8 and compiles
git add -A
git commit -m "Spatial Testing Oracles: base pr-76 + env/infra from pr-73 (per-oracle gating, loaders, tolerances)"

# (Notes to implement)
# - Normalize fixture JSON schema after union; update tests to use shared loader/tolerance helpers. :contentReference[oaicite:1]{index=1}
```

---

## 2) **Spatial Test Suites & Features** — base on `pr-80`, fold in bits from `pr-77`/`pr-78`/`pr-79`

* Rationale: `pr-80` is the strongest chassis; add the separate oracle project (77), correct PostGIS gating via `PostgisFact` (78, with a missing import fix), and keep regeneration tooling (79) while tightening tolerances. 

```bash
# 2A. Merge pr-80 suite backbone and typed fixtures
git merge --no-ff $(git -C "$WORKTREES_ROOT/pr-80-codex-add-spatial-test-suites-and-features-90ok4a" rev-parse HEAD)

# 2B. Add the shared oracle project from pr-77 (centralize deps)
git checkout $(git -C "$WORKTREES_ROOT/pr-77-codex-add-spatial-test-suites-and-features" rev-parse HEAD) -- \
  LiteDB.Spatial.Testing.Oracles/LiteDB.Spatial.Testing.Oracles.csproj \
  LiteDB.Spatial.Testing.Oracles/**

# 2C. Adopt PostGIS skip attribute from pr-78 and fix the missing import
git checkout $(git -C "$WORKTREES_ROOT/pr-78-codex-add-spatial-test-suites-and-features-xfbzfd" rev-parse HEAD) -- \
  LiteDB.Spatial.Core.Tests/PostgisFactAttribute.cs \
  LiteDB.Spatial.Core.Tests/Oracles/PostgisOracle.cs
# Quick fix (one-liner) for the import noted in the report:
git apply - <<'PATCH'
*** Begin Patch
*** Update File: LiteDB.Spatial.Core.Tests/Oracles/PostgisOracle.cs
@@
 using System.Threading.Tasks;
+using LiteDB.Spatial;
*** End Patch
PATCH

# 2D. Bring the regeneration CLI/tooling from pr-79; tighten tolerances later
git checkout $(git -C "$WORKTREES_ROOT/pr-79-codex-add-spatial-test-suites-and-features-9k1ruu" rev-parse HEAD) -- \
  scripts/SpatialOracleTools/**

git add -A
git commit -m "Spatial Suites: base pr-80; add oracle project (pr-77), PostgisFact (pr-78+import fix), regeneration tooling (pr-79)"
```

> Follow-ups you’ll do during review (documented in the report):
>
> * Restore trait wiring (`CategoryAttribute` discoverer) so `--filter Category=oracle` works; align skip semantics; tighten Haversine tolerances to `1e-4 * d + 0.05`. 

---

## 3) **FsCheck Property Tests** — base on `pr-84`, integrate runner (82), plan checks (83), sized gens (81)

* Rationale: `pr-84` is the best property backbone; add `FsCheckPropertyRunner` & richer replay (82), plan-stage assertions/utilities (83), and swap in sized, query-driven generators (81). 

```bash
# 3A. Merge pr-84 as the base
git merge --no-ff $(git -C "$WORKTREES_ROOT/pr-84-codex-add-fscheck-property-tests-for-litedb.spatial-m14epp" rev-parse HEAD)

# 3B. Add runner and failure-payload plumbing from pr-82
git checkout $(git -C "$WORKTREES_ROOT/pr-82-codex-add-fscheck-property-tests-for-litedb.spatial-w1yac6" rev-parse HEAD) -- \
  LiteDB.Spatial.Core.Tests/TestSupport/FsCheckPropertyRunner.cs \
  LiteDB.Spatial.Core.Tests/Differential/Cartesian2D/BoundingBoxDifferentialTests.cs

# 3C. Pull plan-evaluator utilities and assertions from pr-83
git checkout $(git -C "$WORKTREES_ROOT/pr-83-codex-add-fscheck-property-tests-for-litedb.spatial-81ut5t" rev-parse HEAD) -- \
  LiteDB.Spatial.Core.Tests/Support/SpatialPlanEvaluator.cs \
  LiteDB.Spatial.Core.Tests/Differential/Cartesian2D/Cartesian2DPropertyTests.cs

# 3D. Replace/enrich generators with sized variants from pr-81
git checkout $(git -C "$WORKTREES_ROOT/pr-81-codex-add-fscheck-property-tests-for-litedb.spatial" rev-parse HEAD) -- \
  LiteDB.Spatial.Core.Tests/Differential/Cartesian2D/Cartesian2DGenerators.cs

git add -A
git commit -m "FsCheck: base pr-84 + runner (pr-82) + plan coverage (pr-83) + sized generators (pr-81)"
```

> Follow-ups you’ll do during review:
>
> * Add explicit shrinkers for polygon/box cases; standardize `SPATIAL_FSCHECK_SEED=<seed>,<size>` and snapshot payloads. 

---

## 4) **Differential Cartesian3D** — base on `pr-87`, add fixtures (85), tolerance helpers (88), diagnostics (89)

* Rationale: strongest API-level harness = `pr-87`; bring uncapped+mixed fixtures from `pr-85`; adopt scale-aware tolerance helpers from `pr-88`; surface `SpatialCoveringDiagnostics` and engine-vs-oracle parity from `pr-89`. 

```bash
# 4A. Merge pr-87 harness
git merge --no-ff $(git -C "$WORKTREES_ROOT/pr-87-codex-add-tests-for-differential-cartesian3d-xchzvh" rev-parse HEAD)

# 4B. Bring additional lattice fixtures and helpers from pr-85
git checkout $(git -C "$WORKTREES_ROOT/pr-85-codex-add-tests-for-differential-cartesian3d" rev-parse HEAD) -- \
  LiteDB.Spatial.Core.Tests/Differential/Cartesian3D/Cartesian3DLatticeFixture.cs \
  LiteDB.Spatial.Core.Tests/Differential/Cartesian3D/Cartesian3DLatticeFixtureLoader.cs \
  LiteDB.Spatial.Core.Tests/Differential/Cartesian3D/DifferentialFailureRecorder.cs \
  LiteDB.Spatial.Core.Tests/Differential/Cartesian3D/MathNetOracle3D.cs \
  LiteDB.Spatial.Core.Tests/Differential/Cartesian3D/Fixtures/cartesian3d_lattice_dense.json

# 4C. Adopt scale-aware tolerance + diagnostics from pr-88
git checkout $(git -C "$WORKTREES_ROOT/pr-88-codex-add-tests-for-differential-cartesian3d-hzb3dw" rev-parse HEAD) -- \
  LiteDB.Spatial.Core.Tests/Differential/Cartesian3D/Cartesian3DDifferentialTests.cs

# 4D. Add diagnostics + AABB coverage from pr-89
git checkout $(git -C "$WORKTREES_ROOT/pr-89-codex-add-tests-for-differential-cartesian3d-mu1co1" rev-parse HEAD) -- \
  LiteDB.Spatial.Core.Tests/Differential/Cartesian3D/Cartesian3DAabbTests.cs \
  LiteDB.Spatial.Core.Tests/TestSupport/FailureReporter.cs

git add -A
git commit -m "Cartesian3D Differential: base pr-87 + fixtures (pr-85) + scale-aware tolerance (pr-88) + diagnostics/parity (pr-89)"
```

> Follow-ups you’ll do during review:
>
> * Centralize tolerance policy (`membership` vs `parity` deltas) and assert non-fallback for lattice fixtures. 

---

## 5) **Synthetic Grid Locality** — base on `pr-93`, add span metrics (90), uniqueness + top-K checks (92)

* Rationale: `pr-93` offers the best generator+metrics+compact fixtures; add span metrics from `pr-90`; import uniqueness and constant neighborhood checks from `pr-92`; drop `pr-91`. 

```bash
# 5A. Merge pr-93 as base
git merge --no-ff $(git -C "$WORKTREES_ROOT/pr-93-codex-implement-synthetic-grid-tests-for-locality-ae302w" rev-parse HEAD)

# 5B. Bring span-metric helpers from pr-90
git checkout $(git -C "$WORKTREES_ROOT/pr-90-codex-implement-synthetic-grid-tests-for-locality" rev-parse HEAD) -- \
  LiteDB.Spatial.Core.Tests/TestSupport/LocalityMetrics.cs \
  LiteDB.Spatial.Core.Tests/TestSupport/LocalityTestHelpers.cs \
  LiteDB.Spatial.Core.Tests/TestSupport/LocalityAssertions.cs \
  LiteDB.Spatial.Core.Tests/Indexing/Fixtures/locality-fixtures.json

# 5C. Add uniqueness + top-K enforcement from pr-92 (finalizes LocalityTests)
git checkout $(git -C "$WORKTREES_ROOT/pr-92-codex-implement-synthetic-grid-tests-for-locality-6kdpc9" rev-parse HEAD) -- \
  LiteDB.Spatial.Core.Tests/Indexing/LocalityTests.cs \
  LiteDB.Spatial.Core.Tests/TestSupport/SpatialTestAssertions.cs \
  LiteDB.Spatial.Core.Tests/TestSupport/ExplainResultParser.cs \
  LiteDB.Spatial.Core.Tests/Fixtures/uniform_2d_precision8.json \
  LiteDB.Spatial.Core.Tests/Fixtures/uniform_3d_precision6.json

git add -A
git commit -m "Synthetic Grid Locality: base pr-93 + span metrics (pr-90) + uniqueness/top-K assertions (pr-92)"
```

> Follow-ups you’ll do during review:
>
> * Parameterize shapes (anisotropic grids), seedable jitter scenarios, and store both average and minimum-overlap thresholds in fixtures. 

---

## 6) Wire-up + consistency pass (traits, toggles, tolerances)

```bash
# Restore trait filtering & category discoverer (from pr-77/78 into the current tree)
git checkout $(git -C "$WORKTREES_ROOT/pr-77-codex-add-spatial-test-suites-and-features" rev-parse HEAD) -- \
  LiteDB.Spatial.Core.Tests/CategoryAttribute.cs
git add -A && git commit -m "Restore CategoryAttribute discoverer for trait filtering"

# Tighten Haversine/Vincenty tolerances per docs and reports (edit in place)
# (You'll edit test constants to ~ `1e-4 * d + 0.05` for Haversine; keep tight Vincenty.)
git add -A && git commit -m "Align distance tolerances to documented budgets"

# Ensure PostGIS tests skip cleanly when toggles are absent
# (No-op if PostgisFact is already in place; verify TryCreate returns skip reason.)
```



---

## 7) CI sanity: run suites deterministically

```bash
# Run all spatial tests on net8
dotnet test LiteDB.Spatial.Core.Tests -f net8.0 --filter Category=spatial

# Targeted: oracles / geographic / properties / differential / locality
dotnet test LiteDB.Spatial.Core.Tests -f net8.0 --filter Category=oracle
dotnet test LiteDB.Spatial.Core.Tests -f net8.0 --filter Category=geographic
dotnet test LiteDB.Spatial.Core.Tests -f net8.0 --filter Category=property
dotnet test LiteDB.Spatial.Core.Tests -f net8.0 --filter Category=differential
dotnet test LiteDB.Spatial.Core.Tests -f net8.0 --filter Category=locality
```

---

## 8) Finalize

```bash
git push -u origin feat/spatial-revamp-testing
```

---

Want me to also drop a short PR template with the “why these bits?” bullets preloaded from the reports, so reviewers don’t have to spelunk?
