## 1) Executive Summary
- pr-80 offers the strongest structural baseline: typed fixtures, dual-path near parity, and reusable loaders, but it needs gating fixes for PostGIS toggles and trait wiring before it can ship.
- pr-77 contributes the dedicated `LiteDB.Spatial.Testing.Oracles` project and live GeographicLib comparisons that align with the testing brief, yet its PostGIS flow currently passes silently when disabled and reuses a shared table name.
- pr-78 introduces the cleanest opt-in mechanics (`PostgisFact`) and detailed mismatch diagnostics, but a missing `GeoPoint` import breaks the build and must be corrected.
- pr-79 delivers regeneration tooling and runtime GeographicLib checks, though its permissive tolerances and SQL string interpolation weaken the signal from the suite.
- Recommended path: land pr-80 as the chassis, fold in pr-77’s oracle project, adopt pr-78’s skip attribute (after fixing the import), and keep pr-79’s regeneration script with tightened tolerances.

## 2) Coverage Map (features × branch)
| Feature | pr-77 | pr-78 | pr-79 | pr-80 |
| --- | --- | --- | --- | --- |
| Geodesic distance parity (Haversine & Vincenty) | Yes | Yes (fixture cached) | Yes | Yes |
| Bounding box predicate (anti-meridian, polar clamp) | Yes | Yes | Yes | Yes |
| Near predicate vs PostGIS | Partial – returns pass when toggle missing (`LiteDB.Spatial.Core.Tests/Differential/Geographic/GeographicNearPostgisTests.cs:28`) | Partial – `PostgisOracle` signature missing `GeoPoint` import (`LiteDB.Spatial.Core.Tests/Oracles/PostgisOracle.cs:62`) | Partial – relies on env mismatch + string SQL (`LiteDB.Spatial.Core.Tests/Oracles/PostgisOracle.cs:32`) | Partial – returns pass when toggle missing (`LiteDB.Spatial.Core.Tests/Engine/Differential/GeographicNearDifferentialTests.cs:81`) |
| Explain/index assertions in parity tests | Yes | Yes | Yes | Yes |
| Buffers or intersection operators | No | No | No | No |
| Projection / CRS transform checks | No | No | No | No |
| High-volume / randomized datasets | No | No | No | No |

## 3) Design Review (pros/cons per branch)
### pr-77-codex-add-spatial-test-suites-and-features
- **Pro** Dedicated oracle project keeps external dependencies test-only and reusable (`LiteDB.Spatial.Testing.Oracles/LiteDB.Spatial.Testing.Oracles.csproj:1`).
- **Pro** Runtime GeographicLib validation enforces tight 1e-4 tolerances for both modes (`LiteDB.Spatial.Core.Tests/Differential/Geographic/GeodesicDistanceOracleTests.cs:26`).
- **Pro** Bounding-box parity validates explain output and anti-meridian splitting via NTS (`LiteDB.Spatial.Core.Tests/Differential/Geographic/GeographicBoundingBoxOracleTests.cs:55`).
- **Con** PostGIS test exits early instead of skipping, so CI reports false positives when the toggle is absent (`LiteDB.Spatial.Core.Tests/Differential/Geographic/GeographicNearPostgisTests.cs:28`).
- **Con** Shared table name `litedb_spatial_oracle_points` risks collisions under parallel runs (`LiteDB.Spatial.Core.Tests/Differential/Geographic/GeographicNearPostgisTests.cs:23`).
- **Con** Fixture suites remain small (8 distance pairs) and lack regeneration guidance (`LiteDB.Spatial.Core.Tests/fixtures/geodesic_pairs.json:1`).

### pr-78-codex-add-spatial-test-suites-and-features-xfbzfd
- **Pro** `PostgisFactAttribute` cleanly skips PostGIS-dependent tests when opt-in variables are absent (`LiteDB.Spatial.Core.Tests/PostgisFactAttribute.cs:11`).
- **Pro** Bounding-box mismatches surface raw index diagnostics for triage (`LiteDB.Spatial.Core.Tests/Engine/Differential/Geographic/GeographicBoundingBoxParityTests.cs:55`).
- **Pro** Fixture loader supports both loading and saving curated datasets (`LiteDB.Spatial.Core.Tests/FixtureLoader.cs:44`).
- **Con** `PostgisOracle` lacks a `LiteDB.Spatial` import, causing the project to fail compilation (`LiteDB.Spatial.Core.Tests/Oracles/PostgisOracle.cs:62`).
- **Con** Defaulting the connection string to `postgres/postgres` is unsafe for shared environments (`LiteDB.Spatial.Core.Tests/Oracles/PostgisOracle.cs:37`).
- **Con** GeographicLib expectations come from cached numbers, so drift in fixture inputs is harder to detect (`LiteDB.Spatial.Core.Tests/Oracles/GeographicLibOracle.cs:22`).

### pr-79-codex-add-spatial-test-suites-and-features-9k1ruu
- **Pro** Runtime GeographicLib parity avoids stale fixtures while keeping coverage tight (`LiteDB.Spatial.Core.Tests/Oracles/GeographicLibOracle.cs:12`).
- **Pro** `SpatialOracleTools` automates regeneration of oracle datasets and comparison reports (`scripts/SpatialOracleTools/Program.cs:30`).
- **Pro** Near parity exercises both Haversine and Vincenty modes within one test loop (`LiteDB.Spatial.Core.Tests/Engine/GeographicNearPostgisTests.cs:34`).
- **Con** Haversine tolerance is so wide (0.7% + 10 m) that real regressions could slip through (`LiteDB.Spatial.Core.Tests/Engine/GeographicDistanceOracleTests.cs:36`).
- **Con** PostGIS helper depends on `POSTGIS_CONNECTION` while gating on `SPATIAL_DB_TESTS`, leading to confusing configuration and string-built SQL (`LiteDB.Spatial.Core.Tests/Oracles/PostgisOracle.cs:32`).
- **Con** Skip logic reflects into xUnit internals and may throw on future runner versions (`LiteDB.Spatial.Core.Tests/Engine/GeographicNearPostgisTests.cs:81`).

### pr-80-codex-add-spatial-test-suites-and-features-90ok4a
- **Pro** Strongly typed DTO pipeline keeps fixtures and domain models aligned (`LiteDB.Spatial.Core.Tests/Engine/Differential/GeographicFixtures.cs:12`).
- **Pro** Near parity computes manual Vincenty expectations with configurable distance tolerance before checking PostGIS (`LiteDB.Spatial.Core.Tests/Engine/Differential/GeographicNearDifferentialTests.cs:62`).
- **Pro** PostGIS oracle uses per-run temp tables and cleans up via `IAsyncDisposable` (`LiteDB.Spatial.Core.Tests/Engine/Differential/PostgisOracle.cs:30`).
- **Pro** Documentation includes reproducible Python workflow for refreshing geodesic fixtures (`docs/Spatial-Revamp/2-Testing.md:60`).
- **Con** `CategoryAttribute` no longer maps to xUnit traits, so `--filter Category=oracle` stops working (`LiteDB.Spatial.Core.Tests/CategoryAttribute.cs:7`).
- **Con** PostGIS tests simply return when the toggle is absent instead of skipping, masking missing coverage (`LiteDB.Spatial.Core.Tests/Engine/Differential/GeographicNearDifferentialTests.cs:81`).
- **Con** The recommended skip behaviour described in docs is out of sync with the current implementation (`docs/Spatial-Revamp/2-Testing.md:97`).

## 4) Recommended Strategy (as-is vs partial merge)
- Use pr-80’s `Engine/Differential` suite and typed fixtures as the backbone (`LiteDB.Spatial.Core.Tests/Engine/Differential/GeographicNearDifferentialTests.cs:44`).
- Bring in pr-77’s `LiteDB.Spatial.Testing.Oracles` project to centralise external oracle dependencies (`LiteDB.Spatial.Testing.Oracles/LiteDB.Spatial.Testing.Oracles.csproj:1`).
- Replace pr-80’s PostGIS gating with pr-78’s `PostgisFactAttribute`, fixing the missing import by adding `using LiteDB.Spatial;` to `PostgisOracle` (`LiteDB.Spatial.Core.Tests/PostgisFactAttribute.cs:11`).
- Carry over pr-79’s `scripts/SpatialOracleTools` for deterministic fixture regeneration, but tighten Haversine tolerances to the documented `1e-4 * d + 0.05` before landing (`scripts/SpatialOracleTools/Program.cs:30`).
- Reinstate xUnit trait wiring by reusing pr-77/pr-78 `CategoryAttribute` implementation (`LiteDB.Spatial.Core.Tests/CategoryAttribute.cs:14`).

## 5) Migration/Consolidation Plan
- Start from pr-80, port the `CategoryAttribute` trait discoverer and `FixtureLoader.Save` capabilities over pr-80’s loader utilities.
- Swap pr-80’s manual PostGIS toggle checks for the corrected `PostgisFactAttribute`, ensuring `TryCreate` returns skip reasons instead of silent success.
- Integrate pr-77’s oracle project and adjust pr-80’s tests to use the shared helpers rather than local static classes.
- Introduce pr-79’s regeneration tool and document the workflow alongside the existing Python snippet so both .NET and Python paths stay in sync.
- Expand fixtures (geodesic pairs, anti-meridian boxes, near clouds) to meet the acceptance threshold (≥1 000 pairs) using the regenerated data.
- Add buffer/intersection placeholder suites to satisfy the remaining coverage map gaps before calling the effort complete.

## 6) Risks & Mitigations
- **Silent PostGIS bypass**: enforce skip attributes and fail if `SPATIAL_DB_TESTS` is set without a valid connection to avoid false positives (`LiteDB.Spatial.Core.Tests/PostgisFactAttribute.cs:11`).
- **Fixture drift**: mandate regeneration via `SpatialOracleTools` during review and store hashes alongside JSON to detect stale data (`scripts/SpatialOracleTools/Program.cs:60`).
- **Wide tolerances**: align all branches on the documented tolerance budget (`LiteDB.Spatial.Core.Tests/Engine/GeographicDistanceOracleTests.cs:60`).
- **Parallel temp tables**: keep pr-80’s GUID-based PostGIS tables and add explicit `DROP TABLE` in dispose to guard against leftover state (`LiteDB.Spatial.Core.Tests/Engine/Differential/PostgisOracle.cs:77`).
- **Trait filtering regressions**: add a smoke test that asserts `dotnet test --filter Category=oracle` runs only the new suites once `CategoryAttribute` is restored (`LiteDB.Spatial.Core.Tests/CategoryAttribute.cs:14`).

## 7) Decision Table
| Branch | Decision | Notes |
| --- | --- | --- |
| pr-77-codex-add-spatial-test-suites-and-features | Partial | Keep the oracle project and live GeographicLib checks; fix PostGIS gating and expand fixtures before merging. |
| pr-78-codex-add-spatial-test-suites-and-features-xfbzfd | Partial | Salvage the skip attribute and diagnostics after correcting the `GeoPoint` import; remainder blocked by build failure. |
| pr-79-codex-add-spatial-test-suites-and-features-9k1ruu | Partial | Retain the regeneration tool and dual-mode near loop but tighten tolerances and harden PostGIS helper. |
| pr-80-codex-add-spatial-test-suites-and-features-90ok4a | Yes (with fixes) | Use as the structural base once trait wiring and skip semantics are restored. |
