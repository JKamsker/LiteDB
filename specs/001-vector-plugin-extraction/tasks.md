# Tasks: Vector Plugin Isolation

**Input**: Design documents from `/specs/001-vector-plugin-extraction/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Include targeted regression checks where specified below.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish documentation and tooling needed for the migration effort.

- [ ] T001 Create migration guide capturing optional-plugin requirements in docs/vector-plugin-isolation.md
- [ ] T002 Add scripts/verify-vector-clean.ps1 to fail builds when core LiteDB still references "Vector" outside extension points

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Introduce shared abstractions enabling all user stories.

- [ ] T003 Generalize LiteDB/Plugins/Indexing/VectorIndexStrategyDescriptor.cs into a plugin-agnostic CustomIndexStrategy descriptor and registry
- [ ] T004 Add LiteDB/Plugins/Bson/CustomBsonTypeDescriptor.cs plus registration hooks for plugin-defined BSON handlers
- [ ] T005 Extend LiteDB/Plugins/ILitePlugin.cs with IPluginDiagnosticPolicy and add the implementation scaffold in LiteDB/Plugins/PluginDiagnosticPolicy.cs
- [ ] T006 Update LiteDB/Plugins/DefaultPluginContext.cs and LiteDB/Client/Database/LiteDatabaseServices.cs to surface the new registries and diagnostic policy to consumers

**Checkpoint**: Core abstractions ready; user stories can build on these registries.

---

## Phase 3: User Story 1 - Plugin-Only Vector Indexing (Priority: P1) ?? MVP

**Goal**: Ensure all vector APIs live exclusively in LiteDB.Vector while core assemblies remain plugin-neutral.

**Independent Test**: Build referencing LiteDB only and confirm reflection shows no `Vector*` members; add LiteDB.Vector and run vector index creation successfully.

### Implementation

- [ ] T007 [US1] Remove EnsureVectorIndex helpers from LiteDB/Client/Database/Collections/Index.cs and route interception through plugin registries
- [ ] T008 [US1] Delete vector-specific members from LiteDB/Engine/ILiteEngine.cs and LiteDB/Client/Shared/SharedEngine.cs, replacing them with neutral extension hooks
- [ ] T009 [P] [US1] Rebuild LiteDB.Vector/Extensions/LiteCollectionVectorExtensions.cs to use the new CustomIndexStrategy and metadata registries
- [ ] T010 [US1] Move vector compatibility helpers out of LiteDB/Client/Database/VectorCompatibility.cs into LiteDB.Vector/Utils/VectorCompatibility.cs and adjust callers
- [ ] T011 [US1] Add LiteDB.Tests/Client/VectorOptionalityTests.cs validating API surface with and without LiteDB.Vector referenced

**Checkpoint**: Installing LiteDB.Vector re-enables vector indexing; core surface stays clean.

---

## Phase 4: User Story 2 - Pluggable Storage Metadata (Priority: P2)

**Goal**: Treat vector metadata, pages, and diagnostics as plugin-managed assets with deterministic behavior when the plugin is missing.

**Independent Test**: Run rebuild/import with plugin installed to ensure metadata flows through serializers; repeat without plugin to confirm vector collections are refused while other data stays accessible.

### Implementation

- [ ] T012 [US2] Replace baked-in vector metadata slots inside LiteDB/Engine/Pages/CollectionPage.cs with calls to the new metadata serializer registry
- [ ] T013 [P] [US2] Update LiteDB/Engine/Engine/Rebuild.cs and LiteDB/Engine/FileReader/FileReaderV8.cs to reconstruct vector indexes through plugin-provided serializers and strategies
- [ ] T014 [US2] Enforce the "refuse vector operations" policy in LiteDB/Engine/Engine/Index.cs and LiteDB/Engine/Services/SnapShot.cs when plugin assets are absent
- [ ] T015 [US2] Register metadata serializers and diagnostic policy inside LiteDB.Vector/Engine/VectorIndexStrategy.cs and LiteDB.Vector/VectorSearchPlugin.cs
- [ ] T016 [US2] Add LiteDB.Tests/Engine/VectorMetadataCompatibilityTests.cs covering pre-release prototype file access with and without the plugin

**Checkpoint**: Prototype (pre-release) vector databases behave deterministically; metadata is fully plugin-owned.

---

## Phase 5: User Story 3 - Query & BSON Extensibility (Priority: P3)

**Goal**: Move vector expression tokens and BSON types into LiteDB.Vector so other plugins can register their own semantics.

**Independent Test**: Start LiteDB without the plugin and confirm `VECTOR_DIST` parsing fails gracefully; install plugin and verify expressions and BSON serialization succeed via plugin hooks.

### Implementation

- [ ] T017 [US3] Remove vector tokens from LiteDB/Document/Expression/Parser/BsonExpressionType.cs and associated registries, deferring to plugin registrations
- [ ] T018 [US3] Strip vector BSON handling from LiteDB/Document/BsonType.cs, LiteDB/Document/BsonValue.cs, and LiteDB/Document/Bson/BsonTypeRegistry.cs in favor of custom type hooks
- [ ] T019 [P] [US3] Adjust LiteDB/Engine/Query/QueryOptimization.cs and LiteDB/Plugins/QueryPlanningContext.cs to rely on plugin-provided planning rules instead of vector flags
- [ ] T020 [US3] Extend LiteDB.Vector/VectorSearchPlugin.cs to register BSON type descriptors, JSON formatters, and query operators through the new registries
- [ ] T021 [US3] Add LiteDB.Tests/Query/VectorOperatorOptionalityTests.cs to verify planner and parser behavior when the plugin is absent or present

**Checkpoint**: Expression parser and BSON serializer are fully plugin-driven.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Wrap up documentation, tooling, and validation across stories.

- [ ] T022 Refresh README.md and docs/vector-plugin-isolation.md with plugin installation guidance and compatibility notes
- [ ] T023 Follow specs/001-vector-plugin-extraction/quickstart.md end-to-end and capture results in docs/vector-plugin-isolation.md

---

## Dependencies & Execution Order

1. **Setup (Phase 1)** has no prerequisites.
2. **Foundational (Phase 2)** depends on Setup completion and unlocks all user stories.
3. **User Story 1 (Phase 3)** depends on Foundational; delivers the MVP and must complete before shipping.
4. **User Story 2 (Phase 4)** depends on Foundational but can run in parallel with US1 once shared registries exist.
5. **User Story 3 (Phase 5)** depends on Foundational; can proceed in parallel with US2 after US1 ensures API cleanliness.
6. **Polish (Phase 6)** runs after all targeted user stories are complete.

## Parallel Opportunities

- T001 and T002 can run concurrently.
- T003–T006 touch distinct files; mark T004 and T005 as serial due to shared interfaces, but T003 and T006 can overlap once file ownership is clear.
- Within US1, task T009 (plugin extensions) can proceed once T007 begins, while T010 and T011 can run independently.
- US2 tasks T012–T016 mostly modify separate subsystems, enabling T013 and T015 to run in parallel after T012 lands.
- US3 tasks T017–T021 allow T019 to proceed while T017 updates enums; plugin work (T020) can start as soon as registries exist.

## Implementation Strategy

- **MVP Scope**: Complete Phases 1–3; this delivers plugin-only vector indexing with tests ensuring optionality.
- **Incremental Delivery**: After MVP, land Phase 4 for metadata/rebuild flows, then Phase 5 for query & BSON extensibility, validating each increment independently.
- **Testing Cadence**: Run `dotnet test LiteDB.Tests --filter Vector` and `dotnet test LiteDB.Vector.Tests` after each story phase; execute `scripts/verify-vector-clean.ps1` before final polish.
