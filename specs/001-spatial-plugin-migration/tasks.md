# Tasks – Spatial Plugin Migration

## Phase 1 – Setup
- [X] T001 Verify branch `001-spatial-plugin-migration` is active and spec artifacts in `specs/001-spatial-plugin-migration/` remain up to date
- [X] T002 Align solution/package references to remove in-core spatial dependencies (update `LiteDB/LiteDB.csproj`, `LiteDB.Tests/LiteDB.Tests.csproj`, etc.)
- [X] T003 Prepare benchmark and stress harness baselines for comparison (`LiteDB.Benchmarks`, `LiteDB.Stress`)

## Phase 2 – Foundational Infrastructure
- [X] T004 Introduce LINQ resolver factory registry on plugin context (`LiteDB/Plugins/ILitePluginContext.cs`, `LiteDB/Plugins/DefaultPluginContext.cs`)
- [X] T005 Extend `LiteDB/Client/Mapper/Linq/LinqExpressionVisitor.cs` to utilize plugin-provided resolvers with memoization cache
- [X] T006 Expand `LiteDB/Plugins/IQueryPlanningRule` and `LiteDB/Engine/Query/QueryOptimization.cs` to pass structured context and iterate plugin rules
- [X] T007 Implement plugin-managed index interceptor pipeline (`LiteDB/Plugins/DefaultPluginContext.cs`, ensure hooks in `LiteDB/Client/Database/LiteCollection.cs`)
- [X] T008 Ensure `LiteDB.Tests` include regression coverage for plugin-disabled scenarios after hook changes

## Phase 3 – User Story 1: Core build without spatial baggage (Priority P1)

**Goal**: LiteDB core builds/tests succeed with no spatial assemblies and plugin hooks ready.

**Independent Test**: `dotnet build LiteDB.sln -c Release` and `dotnet test LiteDB.sln --settings tests.runsettings` run without spatial plugin references.

- [X] T009 [US1] Remove spatial namespaces/code from core (`LiteDB/Spatial/*`, references in `LiteDB.csproj`)
- [X] T010 [US1] Update client/document/query code to eliminate spatial-specific logic (e.g., remove `IsSpatialPredicate`, tidy `SpatialResolver` references)
- [X] T011 [US1] Adjust tests to skip or re-target spatial cases when plugin disabled (`LiteDB.Tests/Spatial/*`, ensure clear error messaging)
- [X] T012 [US1] Validate build/test pipelines post-removal (record size change of core package)

## Phase 4 – User Story 2: Opt-in spatial plugin restoration (Priority P2)

**Goal**: Spatial plugin restores full functionality using new hooks.

**Independent Test**: Sample app referencing spatial plugin runs `WhereNear` queries and index operations successfully; spatial test suite passes.

- [X] T013 [US2] Implement spatial index interceptor reacting to `GeoPoint` fields (`LiteDB.Spatial.Core/Engine`, plugin registration)
  - Plugin registry/services now enumerate `_spatial_meta` descriptors and register runtime functions; remaining work is to finalize descriptor provisioning and indexing flow.
  - 2025-11-02: `TryCreateDescriptor` unwraps nullable/enumerable members, selects the appropriate initializer, and logs unsupported shapes; still need attribute-driven options/domain handling.
- [X] T014 [P] [US2] Create `ILiteQueryable` extension methods (`WhereNear`, etc.) in spatial plugin (`LiteDB.Spatial/QueryableExtensions.cs`)
  - 2025-11-02: Nullable geometry overloads plus string/BsonExpression wiring added; follow-up tests remain open.
- [X] T015 [US2] Re-register expression functions and planning rules in plugin (`LiteDB.Spatial/SpatialPlugin.cs`)
- [ ] T016 [US2] Update spatial tests to cover interceptor and extensions (`LiteDB.Spatial.Core.Tests/**/*`)
- [ ] T017 [US2] Provide diagnostics for plugin-enabled misconfiguration (`LiteDB.Spatial/SpatialPlugin.cs`, logging)
- [ ] T018 [US2] Prepare sample integration verifying `EnsureIndex + WhereNear` flow (`samples/SpatialApiSample/Program.cs`)

## Phase 5 – User Story 3: Release readiness documentation (Priority P3)

**Goal**: Internal teams have guidance for enabling the spatial plugin in first release.

**Independent Test**: Follow quickstart/migration doc in clean project; plugin works, no spatial references remain in core.

- [ ] T019 [US3] Update quickstart/migration docs including `EnsureIndex` interceptor and `WhereNear` usage (`docs/spatial-*.md`, `specs/001-spatial-plugin-migration/quickstart.md`)
- [ ] T020 [US3] Generate release notes highlighting plugin requirement and migration steps (`docs/spatial-plugin-migration-plan.md`, release template)
- [ ] T021 [US3] Coordinate internal enablement checklist (link to docs in repository README/samples)

## Phase 6 – Polish & Cross-Cutting
- [ ] T022 Run full benchmark/stress suites comparing pre/post migration numbers (`LiteDB.Benchmarks`, `LiteDB.Stress`)
- [ ] T023 Ensure packaging/publishing scripts exclude spatial assemblies from core while including plugin artifacts (`.github/workflows`, `dotnet pack` configs)
- [ ] T024 Finalize tests: run spatial tests with plugin, core tests without plugin, capture results for PR
- [ ] T025 Conduct code review and documentation sanity pass across modified files

## Dependencies
- Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6
- Within Phase 3: Tasks sequential (T009 → T010 → T011 → T012)
- Phase 4 parallel opportunity: T014 can proceed once spatial project references ready but before T013 completes, provided mocked interceptors exist

## Parallel Execution Opportunities
- T014 and T015 can run in parallel with T013 once core hooks are in place.
- Documentation tasks (T019–T021) can run concurrently with validation tasks in Phase 4/6 after core functionality confirmed.

## MVP Scope
- Completing Phase 3 (Tasks T009–T012) delivers an MVP where core builds without spatial baggage and ensures plugin hooks exist, even if spatial functionality is not yet restored.
