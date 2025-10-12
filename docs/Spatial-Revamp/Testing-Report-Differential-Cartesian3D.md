# Testing Report - Differential Cartesian3D

## 1) Executive Summary
- `pr-85`/`pr-86` establish the strongest end-to-end signal today: they exercise the public `Spatial.Near` / `Spatial.WithinBoundingBox` APIs against MathNet oracles and include fixture-driven fallback assertions, but their tolerance model is a fixed epsilon and the scenario set is narrow.
- `pr-87` keeps the public API coverage and improves failure hygiene (sanitised snapshots, explicit MaxCoveringCells checks), yet every scenario is a capped query, so it never validates the non-fallback path and still relies on a constant tolerance.
- `pr-88` goes deep on plan diagnostics (range filtering, BSON coercion, scale-aware tolerance) but replays the planner manually instead of calling the real query APIs-good for instrumentation, risky for regression detection.
- `pr-89` delivers the richest covering diagnostics (`SpatialCoveringDiagnostics`) and the only engine-vs-oracle distance parity test, but it never asserts actual query output.
- **Recommendation:** build on `pr-87`'s API-level structure, fold in `pr-85`'s mixed fallback fixtures, reuse `pr-88`'s scale-aware tolerance helpers, and expose `pr-89`'s diagnostics/engine cross-checks. This hybrid satisfies the numeric policy from `docs/Spatial-Revamp/2-Testing.md` while keeping the assertions grounded in the real pipeline.

## 2) Comparison Framework Review
```
Fixture JSON -> In-memory LiteDatabase -> Spatial descriptor -> Query under test -> Oracle & tolerances -> Failure recorder
```
- **pr-85 / pr-86 (`LiteDB.Spatial.Core.Tests/Differential/Cartesian3D/Cartesian3DDifferentialTests.cs`)**
  1. Load `cartesian3d_lattice_dense.json` through `Cartesian3DLatticeFixtureLoader`.
  2. Create a `SpatialCollectionDescriptor` with fixture options and insert the dataset.
  3. Execute `Spatial.Near` / `Spatial.WithinBoundingBox`; derive oracle distances via `MathNet.Spatial`.
  4. Assert membership parity and capture covering stats via `SpatialCoveringResult`; dump rich failure JSON.
- **pr-87 (`LiteDB.Spatial.Core.Tests/Differential/Cartesian3D/Cartesian3DDifferentialTests.cs`)**
  1. Copy fixtures into the test bin and deserialize strongly-typed records (`lattice-aspects.json`).
  2. Use the public query APIs exactly once per scenario, asserting MaxCoveringCells discipline.
  3. Compare IDs against `MathNet.Numerics.Distance.Euclidean` with per-query tolerances; emit sanitised diffs.
- **pr-88 (`LiteDB.Spatial.Core.Tests/Differential/Cartesian3DDifferentialTests.cs`)**
  1. Load `tests/fixtures/cartesian3d/lattice_v1.json`; build descriptors inline with hard-coded options.
  2. Replay the plan manually: iterate BSON docs, apply `IndexRanges`, bounding prefilter, then oracle distance.
  3. Track `SpatialCoveringMetrics` (range count, cell estimates) and scale-aware deltas; log failures.
- **pr-89 (`LiteDB.Spatial.Core.Tests/Differential/Cartesian3D/Cartesian3DDifferentialTests.cs` + `Cartesian3DAabbTests.cs`)**
  1. Load `cartesian3d-lattice.json`; build descriptor and capture `SpatialCoveringDiagnostics`.
  2. Compare engine distances vs MathNet to assert numerical parity; separately verify stored MBBs and coordinate inequalities.
  3. Use `FailureReporter` to persist structured repro payloads.

## 3) Scenario Coverage Matrix
| Scenario | pr-85 | pr-86 | pr-87 | pr-88 | pr-89 |
| --- | --- | --- | --- | --- | --- |
| Spatial.Near vs MathNet via public API | Yes | Yes (identical tree) | Yes | No (manual replay) | No |
| Spatial.WithinBoundingBox via public API | Yes | Yes | Yes | No (manual replay) | No |
| MaxCoveringCells assertions | Partial (near only) | Partial | Yes (all queries expect fallback) | Partial (fallback observed, not asserted per case) | Partial (checks flag is seen) |
| Tolerance strategy | Fixed `DistanceTolerance` + 1e-9 | Same | Per-query constant (0.05) | Scale-aware (`1e-9 * scale + 1e-9`) | Fixture-level 1e-6 + per-query 1e-9 |
| Bounding box normalisation | Matches coordinates (`~1e-6`) | Same | Ensures min <= max only | Ensures min <= max | Matches coordinates (`~1e-6`) |
| Plan diagnostics surfaced | `SpatialCoveringResult` (estimated/original/final counts, capped flag) | Same | `CoveringCellCount` + fallback flag | `SpatialCoveringMetrics` (cell estimate, max, range count, clipped) | `SpatialCoveringDiagnostics` (requested/returned/effective ranges, cell estimate, max & enumeration fallback) |
| Failure triage | Timestamped JSON with fixture, plan, expected/actual payloads | Same | Sanitised JSON per scenario | Sanitised JSON per scenario | Sanitised JSON per scenario |
| Additional signals | Exercises real query pipeline | Same | Real pipeline; fixture extensibility | Validates BSON conversions & prefilters | Engine-vs-oracle parity; strongest diagnostics |

## 4) Recommended Strategy
**Adopt as-is**
- None of the branches is ready to merge wholesale without gaps (either tolerance, coverage breadth, or pipeline fidelity).

**Compose a hybrid**
1. **Test harness:** start from `pr-87`'s structure (`LiteDB.Spatial.Core.Tests/Differential/Cartesian3D/Cartesian3DDifferentialTests.cs` and fixture loader) to keep `[Theory]` coverage and bin-deployed fixtures.
2. **Fixtures:** merge `pr-85`'s `cartesian3d_lattice_dense.json` (gives capped + uncapped near paths) alongside `pr-87`'s `lattice-aspects.json`; keep `pr-88`'s smaller `lattice_v1.json` for thin-slab edge cases.
3. **Tolerance helpers:** lift the scale-aware delta calculation from `pr-88` (`CalculateTolerance` / `ComputeDistanceDeltas`) and expose it as a shared utility so every branch of the test uses `baselineTol = descriptor.Options.DistanceTolerance` for membership and `deltaTol = 1e-9 * max(scale, 1) + 1e-9` for oracle parity.
4. **Diagnostics:** standardise on `SpatialCoveringDiagnostics` from `pr-89` (requested/returned/effective ranges plus fallback flags) and surface it through `ISpatialQueryPlan`. Retain the richer `WasCapped` assertions from `pr-87` but also assert `UsedEnumerationFallback` stays false for the lattice fixtures.
5. **Bounding checks:** keep `pr-85`/`pr-89`'s strict `_mbb` ~ coordinate checks and `pr-87`'s API-level `Spatial.WithinBoundingBox` parity; run both to guard storage + planner.
6. **Engine regression guard:** port `pr-89`'s engine vs MathNet distance comparison into a dedicated test class so planner regressions and arithmetic regressions fail independently.
7. **Failure recorder:** reuse `FailureReporter`'s sanitised writer but include the richer payload shape from `pr-85` (covering metrics + full fixture metadata) to ease triage.

## 5) Tolerance Policy
- **Membership gate:** treat `descriptor.Options.DistanceTolerance` as the fixed slack when comparing `radius` vs MathNet distance. Fixtures should set this to `1e-9 * scale + 1e-9` when the domain is large; otherwise default to `1e-6` for grids under 100 units.
- **Oracle parity:** after matching IDs, compare engine vs MathNet distances with `deltaTol = 1e-9 * max(|expectedDistance|, |radius|, 1) + 1e-9`, matching the guideline in `docs/Spatial-Revamp/2-Testing.md` (section T2). This keeps the tolerance relative to magnitude yet still clamps the zero-distance case.
- **Bounding boxes:** allow `1e-9` slack on inequality checks but assert stored mins/maxes remain within `1e-6` of the source coordinates to catch normalisation drift.
- **Fallback assertions:** when `SpatialCoveringDiagnostics.UsedMaxCoveringCellsFallback` is true, also require `EffectiveRangeCount <= MaxCoveringCells` and log the `RequestedRangeCount` so over-aggressive merging is visible.

## 6) Risks & Mitigations
- **Manual replay masking query regressions (pr-88):** ensure every differential test also executes the public query API; keep manual plan replays only as supplementary diagnostics.
- **Overly tight tolerances (pr-89's 1e-9) causing flakes:** centralise the tolerance helper and tie it to fixture scale so future datasets can widen slack without touching every assertion.
- **Fixture drift:** all candidate branches rely on bespoke generators. Check in the generator (`scripts/...`) and document the command in `docs/Spatial-Revamp/2-Testing.md`; add a smoke test that ensures fixtures are present in the test output folder.
- **Diagnostics divergence across engines:** normalise on one diagnostics type (`SpatialCoveringDiagnostics`) to avoid scattering conditional logic throughout the codebase.
- **Failure payload explosion (pr-85 dumps entire point sets):** keep the payload but cap it by storing fixture IDs and indices; let the generator reconstruct full datasets when needed.

## 7) Decision Table
| Branch | Decision | Rationale |
| --- | --- | --- |
| pr-85-codex-add-tests-for-differential-cartesian3d | Partial | Strong baseline for exercising the real query pipeline and `_mbb` checks, but needs scale-aware tolerances and broader scenario mix. |
| pr-86-codex-add-tests-for-differential-cartesian3d-t0phsb | Partial | Identical tree to `pr-85`; treat as the same proposal. |
| pr-87-codex-add-tests-for-differential-cartesian3d-xchzvh | Partial | Best harness structure and assertions on MaxCoveringCells, yet lacks uncapped scenarios and dynamic tolerances. |
| pr-88-codex-add-tests-for-differential-cartesian3d-hzb3dw | Partial | Valuable diagnostics and tolerance math, but manual plan execution must complement, not replace, API-level assertions. |
| pr-89-codex-add-tests-for-differential-cartesian3d-mu1co1 | Partial | Keep the covering diagnostics and engine parity test, but add real query checks before merging. |

