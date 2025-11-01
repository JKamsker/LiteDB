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
- Not started; quickstart and migration docs still reference the old in-core spatial model.

### Phase 6 – Cross-cutting polish (T022–T025)
- Benchmarks/stress comparisons, packaging adjustments, final validation, and documentation review are pending future work.

## Current Repository State Highlights
- Core solution (`LiteDB.sln`) builds without any spatial assemblies referenced in `LiteDB` or `LiteDB.Tests`; spatial code now lives purely under `LiteDB.Spatial.*` projects.
- Plugin infrastructure exists but spatial functionality is not yet re-enabled-tests covering spatial features remain absent pending Phase 4 work.
- All Phase 1-3 TODO items are marked complete in `tasks.md`; Phases 4-6 remain open.

## Detailed Next Steps
1. **T013 - Complete descriptor + index interception loop**  
   1.1 Extend `SpatialPluginServices.TryCreateDescriptor` to inspect the entity member’s attributes/configured mapper settings and choose the appropriate initializer (enumerable + nullable unwrap in place as of 2025-11-02; still need attribute-driven overrides and non-point geometries).  
   - Support `GeoPoint`, `GeoPoint3D`, and `BoundingBox` members plus list/array wrappers (`IEnumerable<GeoPoint>` etc.) by peeling collection types before dispatch.  
   - Honor `[SpatialOptions]` attributes or fluent mapper overrides to capture custom engine/precision overrides when calling `SpatialInitializer.Ensure*`.  
   1.2 When a descriptor is created, persist it immediately via `_metadataStore.SaveDescriptor` and re-query to ensure cached state matches on-disk metadata.  
   1.3 Call into `SpatialInitializer` to materialize the Morton index/bounds field; capture both the generated index name and any auxiliary field names in `_geometryFieldsByIndex` for later reuse.  
   1.4 Add defensive logging around unsupported member shapes (e.g., nullable structs, tuples) to aid diagnostics and return `false` so `EnsureIndex` falls back gracefully. *(Implemented 2025-11-02.)*

2. **T014 - Surface LINQ-friendly extensions**  
   2.1 Finish `SpatialQueryableExtensions` by:  
   - Allowing nullable geometry selectors by inserting `Expression.Convert` where needed. *(Implemented 2025-11-02 — GeoPoint?/GeoPoint3D? overloads now flow through resolver.)*  
   - Providing overloads that accept raw field names (`string geometryField`) for dynamic scenarios and route them through `Where(Expression<Func<T,bool>>)` using `BsonExpression.Create`.  
   2.2 Update `SpatialLinqResolver` so `SpatialExpressions.Near/InBox` and the new extension entry points both map to `SPATIAL_NEAR` / `SPATIAL_IN_BOX`.  
   2.3 Add unit coverage in `LiteDB.Spatial.Core.Tests/Linq/SpatialQueryableExtensionsTests.cs` verifying the expression tree translation results in the expected call expressions and that null arguments throw.

3. **T015 - Finalize query-planning rule mechanics**  
   3.1 Ensure `SpatialPredicate.TryParse` understands aliases introduced by `Select` projections (e.g., `$._id` vs. `$._source.Geo`).  
   3.2 Feed descriptor metadata back into the plan: choose geographic vs. Cartesian plan builders, compute radius conversions (meters <-> degrees) using `descriptor.Settings`.  
   3.3 Populate `context.UseIndex(...)` with an `IndexCost` derived from range count and estimated result set; prefer multi-range indexes to highlight partial coverage.  
   3.4 Write regression tests under `LiteDB.Spatial.Core.Tests/Planning/SpatialQueryPlanningRuleTests.cs` using an in-memory database with seeded descriptors; assert `Explain()` returns the plugin range results.  
   3.5 Introduce verbose logging (guarded by plugin log level) to emit the computed ranges and filters for troubleshooting.

4. **T016/T017/T018 - Validation & diagnostics**  
   4.1 Port historical spatial tests to plugin form, splitting into:  
   - Interceptor tests covering automatic index materialization (`EnsureIndex` -> descriptor + metadata).  
   - Query tests for `WhereNear`/`WhereWithinBox` across geographic and 3D datasets, validating both result sets and `Explain()` output.  
   4.2 Implement diagnostic hooks in `SpatialPlugin` to surface missing descriptors or disabled interceptors via context logger and optional exception if `throwOnFailure` flag is set.  
   4.3 Refresh `samples/SpatialApiSample` to register the plugin, call the new extensions, and dump diagnostics when metadata mismatch occurs; include README updates describing the configuration knobs (distance mode, precision).  
   4.4 Capture test run commands/results in this progress log once suites are passing to satisfy Phase 4 acceptance evidence.
