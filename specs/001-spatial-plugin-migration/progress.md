# Spatial Plugin Migration – Progress Report (2025-11-01)

## Completed Scope

### Phase 1 – Setup (T001–T003)
- Verified active branch `001-spatial-plugin-migration` and aligned on latest spec assets.
- Removed core project references to the spatial assemblies (e.g., ceased `LiteDB.Tests` dependency on `LiteDB.Spatial.*`) to prepare for plugin-only delivery.
- Captured pre-migration baselines by running release builds of `LiteDB.Benchmarks` and `LiteDB.Stress`, recording DLL sizes and existing warnings for later comparison; baselines stored in `specs/001-spatial-plugin-migration/baselines.md`.

### Phase 2 – Foundational Infrastructure (T004–T008)
- **Plugin context extensions**: Added LINQ resolver registry and index interceptor registry to `ILitePluginContext`/`DefaultPluginContext`; updated `LiteDatabaseServices` to surface the new registries.
- **LINQ visitor integration**: `LinqExpressionVisitor` now pulls resolvers from plugins via per-database memoized factories (`ConditionalWeakTable` cache) instead of the former hard-coded spatial resolver entries.
- **Query planning**:
  - Introduced `LiteDB.Plugins.QueryPlanningContext` carrying snapshot/query terms/plan to plugin rules.
  - Updated `IQueryPlanningRule` signature to operate on the structured context.
  - Expanded `QueryOptimization` to execute plugin rules, honor their rewrites (indexes, filters, key-only hints), and fall back to built-in selection when no rewrite succeeds.
- **Index interceptors**:
  - Added `IIndexInterceptorRegistry` and default implementation; `LiteCollection<T>` now walks interceptors before invoking the engine’s `EnsureIndex` and shares collection/database context via the new `EnsureIndexContext` wrapper.
- **Regression coverage**: Added `LiteDB.Tests/Plugins/PluginInfrastructureTests.cs` to ensure plugin registries exist with no plugins loaded and that plain `EnsureIndex` still succeeds, preventing regressions for spatial-disabled scenarios.

### Phase 3 – Core spatial removal (T009–T012)
- Deleted `LiteDB/Spatial/*` and all in-core spatial helpers/resolvers; removed spatial-specific logic such as `IsSpatialPredicate` checks.
- Cleansed `LinqExpressionVisitor` and related resolvers of spatial type references; removed obsolete spatial expression method handlers.
- Removed spatial test assets from `LiteDB.Tests` to enforce plugin-only spatial coverage going forward.
- Validated the spatial-free build by running `dotnet build LiteDB.sln -c Release` and `dotnet test LiteDB.Tests -c Release`; recorded the new core DLL footprint (591.00 KB for net8.0) in `baselines.md` to demonstrate package shrinkage.

### Phase 4 – Spatial plugin restoration (T013–T018)
- Completed spatial plugin service wiring, LINQ extensions, and diagnostics to operate exclusively through the plugin hooks (T013–T017).
- Refreshed `samples/SpatialApiSample` to register `SpatialPlugin`, rely on plugin-managed `EnsureIndex` interception, and demonstrate `Query().WhereNear`/`WhereWithinBox` usage for geographic lookups (T018).
- Added a project reference to `LiteDB.Spatial` and verified `dotnet build samples/SpatialApiSample/SpatialApiSample.csproj`, ensuring the sample compiles against the decoupled plugin packages.

## In-Progress / Not Yet Implemented

- **2025-11-01 update**:
  - Rebuilt `SpatialExpressionFunctions` against the new `LiteDB.Spatial` geometry primitives and distance engines, removing dependencies on the retired `GeoShape`/`DistanceFormula` types while covering geographic and 3D Cartesian predicates.
  - Adjusted plugin wiring (`SpatialPlugin`, `SpatialPluginServices`, `SpatialQueryPlanningRule`) to use the public engine identifiers, enumerate metadata through the plugin database, and register expression calls via `BsonExpressionType.Call`.
  - Ported spatial test harnesses away from the legacy `LiteDB.Spatial` alias in core, restoring a clean `dotnet build LiteDB.sln -c Release` despite the new plugin boundary.
- **2025-11-02 update**:
  - `SpatialPluginServices.TryCreateDescriptor` now inspects `BsonMapper` metadata to unwrap nullable/enumerable geometry members, dispatches to the appropriate initializer, and emits plugin log entries when interception succeeds or is skipped.
  - The interceptor no longer double-caches geometry fields when descriptors are provisioned, reducing redundant dictionary churn.
  - Verified `dotnet build LiteDB.sln -c Release` after the refactor; only legacy net461 transitive warnings remain.
  - Added nullable overload support in `SpatialQueryableExtensions`, allowing `GeoPoint?`/`GeoPoint3D?` selectors to flow through the LINQ pipeline without manual `.Value` access while still defaulting to the existing SPATIAL_NEAR/IN_BOX rewrites.
  - Introduced string/BsonExpression overloads for `WhereNear`/`WhereWithinBox`, enabling callers to address geometry fields dynamically while constructing equivalent `SPATIAL_*` predicates at query composition time.
- **2025-11-03 update**:
  - Added spatial configuration surface (`SpatialOptionsAttribute`, `SpatialMemberOptionsBuilder`, entity builder extensions) allowing applications to declare engine selection, precision, tolerances, and domains without core dependencies.
  - Extended `SpatialPluginServices` interceptor to inspect mapper metadata, honor attribute/fluent overrides, and dynamically choose among geographic, Cartesian2D, and Cartesian3D engines (including default 3D domain fallback).
  - Persisted geometry/index/bounding field associations to support `EnsureIndex` reruns and descriptor cache reloads; now logs diagnostics when configuration is missing/incompatible.
  - Ran `dotnet build LiteDB.sln -c Release` to confirm interception changes keep the solution building (only pre-existing net461 support warnings remain).
  - Updated `SpatialPlugin` expression-function registration to use plugin-aware delegates (root/collation/parameter-aware), added `SpatialPlugin.LogDiagnostics` for configuration audits (with optional exceptions), and marked results as scalar so `SPATIAL_NEAR/IN_BOX/WITHIN/INTERSECTS/CONTAINS` parse and execute correctly once the plugin initializes; validated end-to-end via `LiteDB.Spatial.Core.Tests/Plugin/SpatialPluginIntegrationTests.cs` using `dotnet test LiteDB.Spatial.Core.Tests -c Release -f net8.0 --filter SpatialPluginIntegrationTests`.
  - Added `LiteDB.Spatial.Core.Tests/Linq/SpatialQueryableExtensionsTests.cs` to validate expression-tree composition, helper normalization, and guard rails for the new queryable extensions; executed with `dotnet test LiteDB.Spatial.Core.Tests -c Release -f net8.0 --filter SpatialQueryableExtensionsTests`.
- **2025-11-04 update**:
  - Refreshed `samples/SpatialApiSample` to instantiate `LiteDatabase` with `SpatialPlugin`, rely on interceptor-driven `EnsureIndex`, and expose REST endpoints using `Query().WhereNear`/`WhereWithinBox`.
  - Added a `LiteDB.Spatial` project reference to the sample and validated `dotnet build samples/SpatialApiSample/SpatialApiSample.csproj`, confirming the plugin-only integration flow compiles cleanly.
  - Outstanding Phase 4 follow-up is focused on capturing test evidence in `LiteDB.Spatial.Core.Tests` and enhancing diagnostic coverage, with docs and release prep queued for Phase 5.
  - Completed the first release-readiness documentation pass (`specs/001-spatial-plugin-migration/quickstart.md`, `docs/spatial-guide.md`, `docs/spatial-upgrade.md`) so adoptors learn about the `EnsureIndex` interceptor and `WhereNear` extensions; Phase 5 now tracks release notes (T020) and repository enablement touchpoints (T021).
- **2025-11-05 update**:
  - Drafted spatial plugin release notes in `docs/spatial-plugin-migration-plan.md` and captured the copyable bullet list in `docs/release-template.md` so GitHub releases highlight plugin registration, `EnsureIndex` interception, and the new LINQ extensions.
  - Marked T020 complete in `tasks.md`; Phase 5 now focuses on the enablement checklist (T021).
- **2025-11-06 update**:
  - Published `docs/spatial-plugin-enable-checklist.md` consolidating quickstart, upgrade, diagnostics, and benchmark links for internal teams.
  - Added a dedicated "Spatial Plugin Enablement" section to `README.md` and documented the walkthrough in `samples/SpatialApiSample/README.md`, pointing readers at the checklist and sample endpoints.
  - Updated `publish-release.yml` and `publish-prerelease.yml` to pack spatial plugin NuGet artifacts alongside the core package without reintroducing spatial assemblies into `LiteDB.nupkg` (completes T023).
  - Marked T021 complete in `tasks.md`; Phase 5 documentation scope is now fully closed.
- **2025-11-07 update**:
  - Added `--no-wait` support and console redirection guards to `LiteDB.Stress` so the stress harness can run unattended during validation.
  - Executed `LiteDB.Benchmarks` spatial suite with `--job short --spatial-only --filter *SpatialQuery*`, capturing results in `BenchmarkDotNet.Artifacts/...SpatialQueryBenchmarks-report-github.md`; near/within mean latencies held at ~32 μs for dataset size 500 with unchanged allocations.
  - Ran `LiteDB.Stress` scenarios (60 s test-01, 10 s test-02 to curb WAL explosion) via the new flag and recorded summaries in `artifacts_temp/test-01.log` and `artifacts_temp/test-02.log`; appended the numbers to `baselines.md`.
  - Logged benchmark/stress outcomes under `specs/001-spatial-plugin-migration/baselines.md`, marking T022 complete.
- **2025-11-08 update**:
  - Ran `dotnet test LiteDB.Tests/LiteDB.Tests.csproj -c Release --settings tests.runsettings` (core without spatial plugin); net8.0 target reported 232 tests passed with 5 skips, while net461/net481 emitted “no tests found” as expected for framework compatibility shims. Captured warning list (unsupported net461 TFMs, nullable annotations) for the PR notes.
  - Executed `dotnet test LiteDB.Spatial.Core.Tests/LiteDB.Spatial.Core.Tests.csproj -c Release --settings tests.runsettings` to exercise the plugin-enabled suite; 111 tests passed (net8.0). Logged the nullable warning set from `LiteDB.Spatial`/`LiteDB.Spatial.Core.Tests` for follow-up during the documentation/code review.
  - Stored the command transcripts in `specs/001-spatial-plugin-migration/baselines.md` under “2025-11-08 test evidence” to reference in the migration PR checklist.
- **2025-11-09 update**:
  - Enabled `#nullable` contexts and corrected optional signatures across `SpatialPluginServices`, `SpatialInitializer`, `SpatialQueryPlanningRule`, and spatial fixtures to eliminate CS8632/CS860x warnings while retaining explicit null checks for descriptor/index flows.
  - Cleaned up `SpatialQueryableExtensions` XML comments and parameter documentation to remove CS1572/CS1573, ensuring the plugin extensions ship with accurate public docs.
  - Re-ran `dotnet test LiteDB.Tests/LiteDB.Tests.csproj -c Release --settings tests.runsettings` and `dotnet test LiteDB.Spatial.Core.Tests/LiteDB.Spatial.Core.Tests.csproj -c Release --settings tests.runsettings`; both suites now pass with no new warnings from spatial projects (core still surfaces legacy net461 third-party notices). Baselines updated with the clean runs.
  - Performed repository doc sanity sweep (no stray TODOs, README/quickstart still align with plugin flow) and captured ready-for-PR status in tasks/progress trackers.
- **Code review outcome (2025-11-09)**:
  - Spatial plugin scaffolding finalized: descriptor creation/reload, query planning rule, and LINQ extensions now operate under strict nullability with defensive logging.
  - No outstanding TODOs or placeholder code remain; integration sample and spatial tests reflect the finalized plugin surface.

### Phase 5 – Documentation (T019–T021)
- ✅ T019 – Quickstart and migration docs now teach the plugin-based `EnsureIndex` interception flow, LINQ `WhereNear` helpers, and attribute-based configuration.
- ✅ T020 – Release notes template and plan now call out plugin registration, `EnsureIndex` interception, and the new query extensions.
- ✅ T021 – Repository README and sample documentation link to the enablement checklist, quickstart, and diagnostics guidance.

### Phase 6 – Cross-cutting polish (T022–T025)
- ✅ T022 – Benchmark/stress suites executed (spatial short-run benchmarks + stress harness logs added to `baselines.md`).
- ✅ T023 – Release and prerelease workflows now pack the spatial plugin suite while keeping `LiteDB.nupkg` spatial-free.
- ✅ T024 – Captured final core (plugin disabled) vs plugin-enabled spatial test runs; results recorded for PR evidence.
- ✅ T025 – Code and documentation sweep completed (nullability warnings resolved, spatial extension docs cleaned).

## Current Repository State Highlights
- Core solution (`LiteDB.sln`) builds without any spatial assemblies referenced in `LiteDB` or `LiteDB.Tests`; spatial code now lives purely under `LiteDB.Spatial.*` projects.
- Plugin infrastructure exists but spatial functionality is not yet re-enabled-tests covering spatial features remain absent pending Phase 4 work.
- All Phase 1-3 TODO items are marked complete in `tasks.md`; Phases 4-6 remain open.

## Detailed Next Steps
- Phase complete – package migration PR with updated baselines/test evidence.
