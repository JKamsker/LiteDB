# Tasks: Vector Findings Resolution

**Input**: Design documents from `.\specs\001-resolve-vector-findings\`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/plugin-extensibility.yaml

**Tests**: Tests are required where tasks call for regression coverage or quickstart validation. Follow the verification suite noted in the specification.

**Organization**: Tasks are grouped by user story to enable independent implementation and validation of each story slice.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Task can run in parallel (different files, no blocking dependencies)
- **[Story]**: Label user story membership (`US1`, `US2`, `US3`)
- Include exact file paths in descriptions for traceability

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare repository state and working directories.

- [X] T001 Confirm feature branch `001-resolve-vector-findings` is active by inspecting `.git/HEAD`.
- [X] T002 Restore and build baseline solution with `dotnet restore` / `dotnet build LiteDB.sln -c Release`.
- [X] T003 Create staging folder `artifacts_temp/vector-followup/` for upgrade reports and telemetry exports.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Introduce shared extensibility interfaces and context wiring required by all user stories.

**⚠️ CRITICAL**: Complete these tasks before starting any user story work.

- [X] T004 Verify `LiteDB/Plugins/` directory structure exists; create subdirectories `Query/`, `Bson/`, `Storage/`, `Indexing/` if missing.
- [X] T005 Add query metadata accessor contract in `LiteDB/Plugins/Query/IQueryMetadataAccessor.cs`.
- [X] T006 Extend `LiteDB/Plugins/DefaultPluginContext.cs` to expose registration and retrieval APIs for the metadata accessor.
- [X] T007 Introduce plugin BSON type registry interface in `LiteDB/Plugins/Bson/IBsonTypeRegistry.cs` and register it with the plugin context.
- [X] T008 Create page factory registry interface in `LiteDB/Plugins/Storage/IPageFactoryRegistry.cs` with placeholders for factory/metadata hooks.
- [X] T009 Update `LiteDB/Plugins/ILitePlugin.cs` and `LiteDB/Plugins/EnsureIndexContext.cs` to surface the new registry contracts for downstream use.

**Checkpoint**: Foundational registries and context plumbing ready—user story implementation can now proceed.

---

## Phase 3: User Story 1 - Deliver Vector Extensibility (Priority: P1) 🎯 MVP

**Goal**: Provide plugin-owned extension points for query metadata, BSON typing, page factories, and index strategies so vector logic no longer requires core-held fields.

**Independent Test**: Build LiteDB without vector-specific fields and confirm LiteDB.Vector registers the new extension points, enabling vector queries to pass regression tests.

### Implementation for User Story 1

- [ ] T010 [US1] Implement `QueryMetadataBag` in `LiteDB/Plugins/Query/QueryMetadataBag.cs` with typed accessors and versioning.
- [ ] T011 [US1] Refactor `LiteDB/Engine/Query/Query.cs` to remove vector fields and consume `QueryMetadataBag` instead.
- [ ] T012 [P] [US1] Update `LiteDB/Engine/Query/QueryOptimization.cs` and `LiteDB/Plugins/QueryPlanningContext.cs` to propagate metadata bag usage.
- [ ] T013 [US1] Wire the metadata accessor into `LiteDB/Plugins/DefaultPluginContext.cs` and `LiteDB/Plugins/EnsureIndexContext.cs` for plugin consumption.
- [ ] T014 [US1] Add `LiteDB/Document/Bson/BsonTypeRegistry.cs` implementing plugin-managed registrations with fallback shims.
- [ ] T015 [US1] Refactor `LiteDB/Document/BsonType.cs` to delegate vector lookups and registrations to `BsonTypeRegistry`.
- [ ] T016 [P] [US1] Update `LiteDB/Document/BsonValue.cs` and `LiteDB/Document/Json/JsonWriter.cs` to route serialization through the registry.
- [ ] T017 [US1] Introduce page factory support in `LiteDB/Engine/Pages/PageFactoryRegistry.cs` and integrate with `LiteDB/Engine/Pages/BasePage.cs`.
- [ ] T018 [US1] Integrate page factory usage across `LiteDB/Engine/FileReader/FileReaderV8.cs`, `LiteDB/Engine/Engine/Rebuild.cs`, and `LiteDB/Engine/Services/SnapShot.cs`.
- [ ] T019 [US1] Create `LiteDB/Plugins/Indexing/VectorIndexStrategyDescriptor.cs` describing plugin-managed index strategies.
- [ ] T020 [US1] Extend `LiteDB/Plugins/EnsureIndexContext.cs` to register and resolve vector index strategies through the new descriptor.
- [ ] T021 [US1] Refactor `LiteDB/Client/Database/Collections/Index.cs` and `LiteDB/Client/Database/LiteQueryable.cs` to rely on plugin strategies and emit compatibility shims.
- [ ] T022 [US1] Create and implement regression tests in `LiteDB.Tests/Engine/Plugins/QueryMetadataBagTests.cs` validating metadata bag fallback when the plugin is absent.
- [ ] T023 [P] [US1] Create and implement `LiteDB.Vector.Tests/Integration/VectorRegistryTests.cs` to confirm plugin registration flows exercise all new extension points.
- [ ] T023b [US1] Add integration test in `LiteDB.Tests/Engine/Plugins/PluginAbsentTests.cs` validating deterministic errors when vector operations run without plugin loaded.

**Checkpoint**: Plugin extensibility infrastructure working end-to-end; core no longer requires vector-specific fields to execute vector workloads with the plugin available.

---

## Phase 4: User Story 2 - Migrate Vector Runtime to Plugin (Priority: P2)

**Goal**: Relocate remaining vector runtime types, helpers, and storage structures into `LiteDB.Vector`, leaving only compatibility shims in the core.

**Independent Test**: `rg "Vector" LiteDB` shows only compatibility shims, and plugin integration tests confirm functionality matches pre-migration behavior.

### Implementation for User Story 2

- [ ] T024 [US2] Move `LiteDB/Engine/Services/VectorIndexServiceFactory.cs` into `LiteDB.Vector/Engine/Services/VectorIndexServiceFactory.cs` with plugin registration logic.
- [ ] T025 [P] [US2] Relocate vector LINQ helpers from `LiteDB/Client/Database/LiteQueryable.cs` and `LiteDB/Client/Database/LiteRepository.cs` into `LiteDB.Vector/Extensions/QueryableExtensions.cs`.
- [ ] T026 [US2] Shift `LiteDB/Document/BsonVector.cs` and `LiteDB/Utils/Extensions/BufferSliceExtensions.cs` into appropriate namespaces under `LiteDB.Vector`.
- [ ] T027 [US2] Move vector storage structures (`LiteDB/Engine/Pages/VectorIndexPage.cs`, `LiteDB/Engine/Structures/VectorIndexNode.cs`, `LiteDB/Engine/Structures/VectorIndexMetadata.cs`) into `LiteDB.Vector/Engine`.
- [ ] T028 [US2] Implement vector strategy registration in `LiteDB.Vector/Engine/VectorIndexStrategy.cs` using the new registries.
- [ ] T029 [US2] Remove vector-specific `InternalsVisibleTo` entries from `LiteDB/LiteDB.csproj`.
- [ ] T029b [US2] Verify `dotnet build LiteDB.Vector -c Release` succeeds with zero errors/warnings after `InternalsVisibleTo` removal.
- [ ] T030 [US2] Execute `rg "Vector" LiteDB` and update `specs/001-vector-core-cleanup/SUMMARY.md` to document zero outstanding components.
- [ ] T031 [P] [US2] Update `LiteDB.Vector.Tests/Integration/VectorIndexTests.cs` to cover relocated runtime behaviors and ensure parity.

**Checkpoint**: Vector runtime code is isolated within the plugin project, and the core dependency surface is free from vector-specific implementations.

---

## Phase 5: User Story 3 - Ship Upgrade & Compatibility Guardrails (Priority: P3)

**Goal**: Provide automated upgrade guidance, regression coverage, and diagnostics so deployments can adopt the plugin-backed implementation without regressions.

**Independent Test**: Run the upgrade script, verification checklist, and tests to confirm successful migration plus deterministic failures when the plugin is missing.

### Implementation for User Story 3

- [ ] T032 [US3] Implement C# migration helpers in `scripts/vector/MigrationHelpers.cs` to handle database metadata relocation and validation.
- [ ] T033 [US3] Implement `scripts/vector/Invoke-VectorUpgrade.ps1` orchestrating manifest-driven upgrades and validation commands using the migration helpers.
- [ ] T034 [US3] Create `specs/001-resolve-vector-findings/migration/upgrade-manifest.json` documenting ordered upgrade steps and verification hooks.
- [ ] T035 [P] [US3] Update `specs/001-vector-core-cleanup/verification/build-validation.json` with new plugin-first validation requirements.
- [ ] T036 [US3] Add structured diagnostics in `LiteDB/Engine/Engine/LiteEngine.cs` when vector operations run without a registered plugin.
- [ ] T037 [P] [US3] Add telemetry helpers in `LiteDB.Vector/Utils/VectorTelemetry.cs` to emit incompatibility warnings and remediation guidance.
- [ ] T038 [US3] Execute the quickstart flow from `specs/001-resolve-vector-findings/quickstart.md` and archive logs to `artifacts_temp/vector-followup/upgrade-report.md`.
- [ ] T039 [US3] Update `specs/001-vector-core-cleanup/SUMMARY.md` and `specs/001-vector-core-cleanup/diagrams/component-dependencies.md` to mark gap resolution.

**Checkpoint**: Upgrade tooling, diagnostics, and documentation verify the plugin-backed implementation is production-ready.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final documentation, packaging, and quality improvements affecting multiple stories.

- [ ] T040 [P] Refresh `docs/plugins/plugin-development.md` with guidance for query metadata bags, BSON registries, and page factories.
- [ ] T041 Verify `System.Threading.Tasks.Extensions` package reference exists in `LiteDB/LiteDB.csproj` for `netstandard2.0` ValueTask support; add if missing.
- [ ] T042 Run `dotnet pack LiteDB/LiteDB.csproj -c Release` to verify packaging after vector migration.
- [ ] T043 Document follow-up learnings and remaining shims in `specs/001-resolve-vector-findings/research.md`.

---

## Dependencies & Execution Order

- **Phase 1 → Phase 2**: Complete repository setup before adding shared registries.
- **Phase 2 → User Stories**: User stories depend on the new interfaces and context wiring.
- **User Stories**: Execute in priority order (US1 → US2 → US3) to maintain MVP momentum; US2 requires US1 registries, US3 relies on migration artifacts from US2.
- **Polish**: Execute after targeted user stories reach acceptance to prepare release deliverables.

---

## Parallel Opportunities

- In US1, tasks T012, T016, and T023 are parallelizable once core registry scaffolding exists.
- US2 allows T025 and T031 to proceed concurrently after T024 migrates the service factory.
- US3 tasks T035 and T037 can run in parallel after T033 defines the upgrade automation.

---

## Implementation Strategy

- **MVP Focus**: Finish US1 (extensibility) before touching runtime migrations; this unlocks plugin registration without destabilizing the core.
- **Incremental Delivery**: Merge US1 changes, then US2 migrations, validating each with regression tests before proceeding.
- **Operational Readiness**: Use US3 tasks to finalize upgrade tooling and diagnostics prior to release tagging, reducing production risk.
