## 1. Executive Summary
- pr-93-codex-implement-synthetic-grid-tests-for-locality-ae302w provides the strongest foundation: uniform grid generators, recorded metrics for overlap and window density, and manageable fixtures.
- pr-90-codex-implement-synthetic-grid-tests-for-locality contributes useful span metrics (average and worst-case window) but its fixtures are undersized and limited to square grids.
- pr-92-codex-implement-synthetic-grid-tests-for-locality-6kdpc9 adds larger grids and sanity checks for duplicate codes, yet the inline coordinate fixtures are unwieldy and its locality metric ignores contiguous windows.
- pr-91-codex-implement-synthetic-grid-tests-for-locality-0l0524 is too small-scale and leaves its candidate-reduction metric unused.
- Recommended path: base the suite on pr-93, lift span metrics and helper ergonomics from pr-90, carry over the uniqueness/consistent-window assertions from pr-92 (but rework their metric), and retire pr-91.

## 2. Scenario & Metric Inventory

### pr-90-codex-implement-synthetic-grid-tests-for-locality (origin/codex/implement-synthetic-grid-tests-for-locality)
| Scenario | Dim | Grid / Points | Precision bits | Neighbor settings | Recorded metrics | Assertions & notes |
| --- | --- | --- | --- | --- | --- | --- |
| cartesian2d-gridSize8 | 2 | 8×8 (64 pts) | 10 | neighborCount=12; window inferred as ±12 positions | expectedOverlap=0.85026; avgWindowSpan=21.5625 | Exact Morton code match; average overlap ≥ expected-0.01; average span ≤ baseline×1.05; worst span ≤ averageSpan×2 |
| cartesian3d-gridSize4 | 3 | 4×4×4 (64 pts) | 8 | neighborCount=18; window inferred as ±18 positions | expectedOverlap=0.755208; avgWindowSpan=30.65625 | Same assertions as above; grid generator enforces equal size per axis |

### pr-91-codex-implement-synthetic-grid-tests-for-locality-0l0524 (origin/codex/implement-synthetic-grid-tests-for-locality-0l0524)
| Scenario | Dim | Grid / Points | Precision bits | Neighbor settings | Recorded metrics | Assertions & notes |
| --- | --- | --- | --- | --- | --- | --- |
| cartesian2d-4x4 | 2 | 4×4 (16 pts) | 4 | neighborCount=4; mortonWindowRadius=3 | targetAverageOverlap=0.75; targetMinimumOverlap=0.5 | Exact Morton code match; average overlap ≥ target; minimum overlap ≥ target |
| cartesian3d-3x3x3 | 3 | 3×3×3 (27 pts) | 3 | neighborCount=6; mortonWindowRadius=4 | targetAverageOverlap=0.53; targetMinimumOverlap=0.33 | Same assertions; candidate reduction helper unused |

### pr-92-codex-implement-synthetic-grid-tests-for-locality-6kdpc9 (origin/codex/implement-synthetic-grid-tests-for-locality-6kdpc9)
| Scenario | Dim | Grid / Points | Precision bits | Neighbor settings | Recorded metrics | Assertions & notes |
| --- | --- | --- | --- | --- | --- | --- |
| uniform_2d_precision8 | 2 | 16×16 (256 pts) | 8 | topK=4 (code-distance ranking) | baselineAverage=0.52539; minimumOverlap=0.5 | Exact Morton code match; average overlap ≈ baseline ±0.01; minimum overlap ≥ 0.75×target; verifies duplicate-free codes and constant Morton neighborhood size; coordinates embedded in fixture |
| uniform_3d_precision6 | 3 | 8×8×8 (512 pts) | 6 | topK=6 (code-distance ranking) | baselineAverage=0.35677; minimumOverlap=0.33 | Same assertions; large JSON fixture stores every coordinate and code |

### pr-93-codex-implement-synthetic-grid-tests-for-locality-ae302w (origin/codex/implement-synthetic-grid-tests-for-locality-ae302w)
| Scenario | Dim | Grid / Points | Precision bits | Neighbor settings | Recorded metrics | Assertions & notes |
| --- | --- | --- | --- | --- | --- | --- |
| cartesian2d_16x16_prec10 | 2 | 16×16 (256 pts) | 10 | neighborCount=8; windowRadius=12 | avgOverlap=0.79102; avgWindow=23.3906; minOverlap=0.78; maxWindow=26 | Exact Morton code match; average overlap ≈ baseline ±0.02; average overlap ≥ min; average window ≤ max and ≈ baseline ±2 |
| cartesian3d_8x8x8_prec9 | 3 | 8×8×8 (512 pts) | 9 | neighborCount=12; windowRadius=18 | avgOverlap=0.64046; avgWindow=35.3320; minOverlap=0.62; maxWindow=40 | Same assertions; fixtures supply only codes + metrics; grid generated on demand |

## 3. Performance & Flakiness Notes
- All branches are deterministic; no random seeds are introduced. Introduce seedable jitter before expanding coverage to avoid hidden flakiness.
- pr-90 executes quickly (<5 ms per fixture) but the 8×8/4×4 grids lack stress on precision-bit boundaries.
- pr-91 runs instantly yet risks false negatives because 16–27 point clouds are too small to surface Morton ordering regressions.
- pr-92 loads ~3,500 lines of JSON per run; the O(n²) overlap computation on 512 points is still fast (<50 ms) but fixture bloat makes diffs fragile and invites merge conflicts.
- pr-93 matches pr-92’s runtime envelope while keeping fixtures concise; no observed flakiness, but current tolerances only guard averages, not per-point outliers.

## 4. Recommended Strategy
- Adopt pr-93’s structure (grid generator, compact fixtures, overlap + window metrics) as the baseline suite.
- Fold in pr-90’s `AverageSpan`/`WorstCaseSpan` calculation to monitor contiguous window growth alongside the existing average window metric.
- Carry over pr-92’s assertions that ensure Morton codes are unique and that every Morton neighborhood hits the requested top-K size; re-express their metric using contiguous windows rather than global code-distance sorting.
- Reuse pr-91’s `CandidateReductionReport` to assert index range selectivity once the LINQ-side tests exercise actual query pipelines.
- Extend the fixture model to accept per-axis shapes, seeded jitter/noise envelopes, and metric tolerances for both average and minimum overlaps so new scenarios can be scripted rather than hard-coded.

## 5. Parameter Set Proposal
| Scenario | Dim | Grid shape | Seed / noise | Neighbor / window plan | Target metrics |
| --- | --- | --- | --- | --- | --- |
| uniform-2d-baseline | 2 | 16×16 | deterministic | neighborCount=8; windowRadius=12 | avgOverlap ≥ 0.78; avgWindow ≤ 26; worstSpan ≤ 32 |
| uniform-2d-dense | 2 | 32×32 | deterministic | neighborCount=10; windowRadius=18 | avgOverlap ≥ 0.72; avgWindow ≤ 40; worstSpan ≤ 52 |
| uniform-3d-baseline | 3 | 8×8×8 | deterministic | neighborCount=12; windowRadius=18 | avgOverlap ≥ 0.62; avgWindow ≤ 40; worstSpan ≤ 60 |
| anisotropic-3d | 3 | 12×6×6 | deterministic | neighborCount=12; windowRadius=20 | avgOverlap ≥ 0.58; avgWindow ≤ 48; verify slice monotonicity |
| jittered-2d | 2 | 24×24 | seed=20240517; add ±0.01 jitter | neighborCount=10; windowRadius=20 | avgOverlap ≥ 0.68; minOverlap ≥ 0.45; capture failure snapshot on breach |
| jittered-geo-projection | 2 (lon/lat) | 1° grid around equator | seed=20240517; project + jitter | neighborCount=12; windowRadius=24 | avgOverlap ≥ 0.65; candidate reduction ≥ 60 %; compare against GeographicLib great-circle ordering |

## 6. Risks & Mitigations
- Hard-coded Morton code arrays will break intentionally when encoder bit layouts change; mitigate by regenerating fixtures via a tooling script and recording rationale per update.
- Floating-point rounding (e.g., 1/3 steps) can drift across runtimes; keep tolerances ≥1e-6 for overlap/window comparisons and persist metrics at double precision.
- Large JSON fixtures (pr-92 style) hurt maintainability; prefer programmatic grid generation plus minimal metadata to reduce review friction.
- Current tests only probe Euclidean locality; add follow-on cases covering L1 (Manhattan) and geodesic neighborhoods through the LINQ/query layer to satisfy the spatial revamp acceptance criteria.
- Absence of per-point diagnostics makes debugging hard; emit compact CSV or attach failing point indices to assertion messages when overlaps fall below guardrails.

## 7. Decision Table
| Branch | Recommendation | Rationale |
| --- | --- | --- |
| pr-90-codex-implement-synthetic-grid-tests-for-locality | Partial merge | Keep span metrics and lightweight fixture loader; replace small square grids with richer shapes from pr-93 and new proposals. |
| pr-91-codex-implement-synthetic-grid-tests-for-locality-0l0524 | No | Datasets are too small, and key metrics (candidate reduction) are unused, offering no additional coverage beyond other branches. |
| pr-92-codex-implement-synthetic-grid-tests-for-locality-6kdpc9 | Partial merge | Reuse uniqueness checks and top-K enforcement but avoid the massive coordinate fixtures and adjust the locality metric to contiguous windows. |
| pr-93-codex-implement-synthetic-grid-tests-for-locality-ae302w | Yes (base) | Provides scalable fixture model, balanced grid sizes, and meaningful overlap/window thresholds aligned with the spatial testing addendum. |
