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
- **Partially scaffolded**:
  - Added `LiteDB.Spatial.Plugin.SpatialPlugin` with placeholder registration for expression functions, LINQ resolvers, query planning rules, and index interceptors.
  - Created `SpatialPluginServices`, `SpatialPluginRegistry`, and supporting runtime helpers (e.g., `SpatialInitializer`, `SpatialExpressionFunctions`) to bridge plugin extensions with core metadata (`SpatialMetadataStore`) and enable on-demand descriptor provisioning.
  - Introduced an initial `SpatialLinqResolver` that maps `SpatialExpressions` to the new plugin-managed functions.
- **Outstanding**:
  - Finish `SpatialPluginServices.TryCreateDescriptor` logic (currently limited to `GeoPoint`/`GeoPoint3D` and requires richer domain handling).
  - Implement real engagement with `SpatialMetadataStore` and the concrete spatial engines (`GeographicEngine`, `Cartesian2DEngine`, `Cartesian3DEngine`) to produce query plans and backfill indexes.
  - Build actual `ILiteQueryable` spatial extension methods (`WhereNear`, `WhereWithinBox`, etc.) in `LiteDB.Spatial` to replace the removed core helpers.
  - Author a concrete `SpatialQueryPlanningRule` that inspects `QueryPlanningContext` terms and emits plugin-managed `Index` instances.
  - Wire in diagnostics for misconfiguration (e.g., missing descriptors, conflicting engines) and integrate sample usage (`samples/SpatialApiSample`).

### Phase 5 – Documentation (T019–T021)
- ✅ T019 – Quickstart and migration docs now teach the plugin-based `EnsureIndex` interception flow, LINQ `WhereNear` helpers, and attribute-based configuration.
- ⏳ T020 – Draft release notes summarising plugin requirements and migration steps.
- ⏳ T021 – Update README/samples with enablement pointers to the refreshed docs.

### Phase 6 – Cross-cutting polish (T022–T025)
- Benchmarks/stress comparisons, packaging adjustments, final validation, and documentation review are pending future work.

## Current Repository State Highlights
- Core solution (`LiteDB.sln`) builds without any spatial assemblies referenced in `LiteDB` or `LiteDB.Tests`; spatial code now lives purely under `LiteDB.Spatial.*` projects.
- Plugin infrastructure exists but spatial functionality is not yet re-enabled-tests covering spatial features remain absent pending Phase 4 work.
- All Phase 1-3 TODO items are marked complete in `tasks.md`; Phases 4-6 remain open.

## Detailed Next Steps
1. **T020 – Release notes**  
   - Summarise the spatial plugin split, mandatory plugin registration, and the new `EnsureIndex` interception workflow.  
   - Call out migration steps (attributes/fluent options, `WhereNear` replacements) and link to `docs/spatial-upgrade.md`, `docs/spatial-guide.md`, and the quickstart.  
   - Update `docs/spatial-plugin-migration-plan.md` and the release template to include these talking points.

2. **T021 – Enablement checklist**  
   - Add README and sample repository pointers that direct teams to the refreshed docs.  
   - Ensure internal enablement checklists reference the quickstart, upgrade guide, and diagnostics documentation for first-line support.

3. **Phase 6 preparation (T022–T025)**  
   - Plan benchmark/stress comparisons once docs/release notes are finalised.  
   - Audit packaging scripts so core packages exclude spatial binaries while plugin nupkgs continue to ship required assets.  
   - Schedule final validation runs (core without plugin, spatial with plugin) and documentation/code review sweep before opening the PR.
