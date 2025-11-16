# Tasks: Vector Plugin Isolation

**Input**: Design documents from `/specs/001-vector-plugin-extraction/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Include targeted regression checks where specified below.

**Task Markers**:
- `[P]` = Parallel-ready task that can execute concurrently with other tasks once dependencies are met
- `[US1]`, `[US2]`, `[US3]` = Maps to User Story 1, 2, or 3 in spec.md

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish documentation and tooling needed for the migration effort.

- [X] T001 Create migration guide capturing optional-plugin requirements in docs/vector-plugin-isolation.md
- [X] T002 Add scripts/verify-vector-clean.ps1 to fail builds when core LiteDB still references "Vector" outside extension points

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Introduce shared abstractions enabling all user stories.

- [X] T003 Generalize LiteDB/Plugins/Indexing/CustomIndexStrategyDescriptor.cs into a plugin-agnostic CustomIndexStrategy descriptor and registry (ICustomIndexStrategyRegistry)
- [X] T003a Create LiteDB/Plugins/Indexing/IPluginIndexMetadataRegistry.cs with PluginIndexMetadataDescriptor for metadata serialization separate from strategy operations
- [ ] T004 Add LiteDB/Plugins/Bson/CustomBsonTypeDescriptor.cs plus registration hooks (ICustomBsonTypeRegistry) for plugin-defined BSON handlers
- [ ] T004a Add LiteDB/Plugins/Storage/IPageTypeRegistry.cs with PageFactoryRegistration and PageConstructionContext for plugin-owned page types
- [ ] T004b Create LiteDB/Plugins/Query/ISqlFunctionRegistry.cs with SqlFunctionRegistration for plugin-defined SQL functions
- [ ] T004c Create LiteDB/Plugins/Query/IQueryOperatorRegistry.cs with QueryOperatorRegistration for plugin-defined operators (e.g., VECTOR_KNN)
- [ ] T004d Create LiteDB/Plugins/Query/IQueryCostModelRegistry.cs with QueryCostModelRegistration for plugin cost hooks
- [ ] T005 Extend LiteDB/Plugins/ILitePlugin.cs with IPluginDiagnosticPolicy and add the implementation scaffold in LiteDB/Plugins/PluginDiagnosticPolicy.cs
- [ ] T005a Add conflict detection mechanism to all registries: validate reserved code ranges (BSON 0x90-0x9F, page 0xE0-0xEF for LiteDB.Vector) and throw InvalidOperationException on overlaps
- [ ] T006 Update LiteDB/Plugins/DefaultPluginContext.cs and LiteDB/Client/Database/LiteDatabaseServices.cs to surface the new registries and diagnostic policy to consumers
- [ ] T006a Remove InternalsVisibleTo declarations between LiteDB and LiteDB.Vector assemblies; verify all cross-assembly access now flows through plugin registries

**Checkpoint**: Core abstractions ready; user stories can build on these registries.

---

## Phase 3: User Story 1 - Plugin-Only Vector Indexing (Priority: P1) ?? MVP

**Goal**: Ensure all vector APIs live exclusively in LiteDB.Vector while core assemblies remain plugin-neutral.

**Independent Test**: Build referencing LiteDB only and confirm reflection shows no `Vector*` members; add LiteDB.Vector and run vector index creation successfully.

### Implementation

- [ ] T007 [US1] Remove EnsureVectorIndex helpers from LiteDB/Client/Database/Collections/Index.cs and route interception through plugin registries
- [ ] T008 [US1] Delete vector-specific members from LiteDB/Engine/ILiteEngine.cs and LiteDB/Client/Shared/SharedEngine.cs, replacing them with neutral extension hooks
- [ ] T009 [P] [US1] Rebuild LiteDB.Vector/Extensions/LiteCollectionVectorExtensions.cs: update EnsureIndex/DropIndex to call plugin-registered CustomIndexStrategy delegates; update to use IPluginIndexMetadataRegistry for metadata serialization
- [ ] T010 [US1] Move vector compatibility helpers out of LiteDB/Client/Database/VectorCompatibility.cs into LiteDB.Vector/Utils/VectorCompatibility.cs and adjust callers
- [ ] T011 [US1] Add LiteDB.Tests/Client/VectorOptionalityTests.cs validating API surface with and without LiteDB.Vector referenced (verify no Vector* symbols exposed in LiteDB-only builds via reflection)

**Checkpoint**: Installing LiteDB.Vector re-enables vector indexing; core surface stays clean.

---

## Phase 4: User Story 2 - Pluggable Storage Metadata (Priority: P2)

**Goal**: Treat vector metadata, pages, and diagnostics as plugin-managed assets with deterministic behavior when the plugin is missing.

**Independent Test**: Run rebuild/import with plugin installed to ensure metadata flows through serializers; repeat without plugin to confirm vector collections are refused while other data stays accessible.

### Implementation

- [ ] T012 [US2] Replace baked-in vector metadata slots inside LiteDB/Engine/Pages/CollectionPage.cs with calls to IPluginIndexMetadataRegistry; update metadata storage format to {pluginIdLength:byte}{pluginId:utf8}{payloadLength:ushort}{payload:bytes}
- [ ] T013 [P] [US2] Update LiteDB/Engine/Engine/Rebuild.cs to reconstruct indexes through IPluginIndexMetadataRegistry for deserialization and ICustomIndexStrategyRegistry for rebuild delegates
- [ ] T013a [P] [US2] Update LiteDB/Engine/FileReader/FileReaderV8.cs to deserialize plugin-owned metadata via IPluginIndexMetadataRegistry; emit LITE2002 when serializer is missing
- [ ] T014 [US2] Enforce the "refuse vector operations" policy in LiteDB/Engine/Engine/Index.cs and LiteDB/Engine/Services/SnapShot.cs when plugin assets are absent; consult IPluginDiagnosticPolicy for behavior (RefuseDatabase vs RefuseOperations vs AllowIfSafe)
- [ ] T015 [US2] Register metadata serializers (PluginIndexMetadataDescriptor for "vector.hnsw"), page factories (PageFactoryRegistration for code 0xE0), and diagnostic policy inside LiteDB.Vector/VectorSearchPlugin.cs Initialize() method
- [ ] T016 [US2] Add LiteDB.Tests/Engine/VectorMetadataCompatibilityTests.cs covering pre-release prototype file access with and without the plugin; verify single warning logged and LITE2002 on vector operations
- [ ] T016a [US2] Add LiteDB.Tests/Engine/BehaviorMatrixIntegrationTests.cs implementing each row of the behavior matrix from spec.md lines 113-121; include compaction/shrink scenarios

**Checkpoint**: Prototype (pre-release) vector databases behave deterministically; metadata is fully plugin-owned.

---

## Phase 5: User Story 3 - Query & BSON Extensibility (Priority: P3)

**Goal**: Move vector expression tokens and BSON types into LiteDB.Vector so other plugins can register their own semantics.

**Independent Test**: Start LiteDB without the plugin and confirm `VECTOR_DIST` parsing fails gracefully; install plugin and verify expressions and BSON serialization succeed via plugin hooks.

### Implementation

- [ ] T017 [US3] Remove vector tokens from LiteDB/Document/Expression/Parser/BsonExpressionType.cs and associated registries; update expression parser to consult IQueryOperatorRegistry for unknown operators
- [ ] T018 [US3] Strip vector BSON handling from LiteDB/Document/BsonType.cs, LiteDB/Document/BsonValue.cs, and LiteDB/Document/Bson/BsonTypeRegistry.cs; update BSON reader/writer to consult ICustomBsonTypeRegistry for unknown type codes
- [ ] T019 [P] [US3] Adjust LiteDB/Engine/Query/QueryOptimization.cs and LiteDB/Plugins/QueryPlanningContext.cs to consult IQueryCostModelRegistry when evaluating custom index types; remove hardcoded vector planner logic
- [ ] T020 [US3] Extend LiteDB.Vector/VectorSearchPlugin.cs Initialize() to register: (a) CustomBsonTypeDescriptor for type code 0x90, (b) SqlFunctionRegistration for VECTOR_DIST/VECTOR_SIM, (c) QueryOperatorRegistration for VECTOR_KNN, (d) QueryCostModelRegistration for "vector.hnsw"
- [ ] T021 [US3] Add LiteDB.Tests/Query/VectorOperatorOptionalityTests.cs to verify: (a) parsing VECTOR_DIST fails without plugin, (b) planner ignores vector indexes without plugin, (c) BSON serialization of vector types fails without plugin

**Checkpoint**: Expression parser and BSON serializer are fully plugin-driven.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Wrap up documentation, tooling, and validation across stories.

- [ ] T022 Refresh README.md and docs/vector-plugin-isolation.md with plugin installation guidance and compatibility notes; document breaking changes for prerelease vector users
- [ ] T023 Follow specs/001-vector-plugin-extraction/quickstart.md end-to-end and capture results in docs/vector-plugin-isolation.md; verify migration paths work as documented
- [ ] T024 Update scripts/verify-vector-clean.ps1 to: (a) exclude comments/strings/docs from grep, (b) only check compiled code paths, (c) allow extension point declarations in LiteDB/Plugins/
- [ ] T025 Add LiteDB.Tests/Plugins/ReservedIdentifierConflictTests.cs to verify reserved code range validation: test BSON code conflicts, page code conflicts, and valid plugin registrations

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
- T003–T006a touch distinct files; T003/T003a (indexing registries), T004/T004a-d (BSON/page/query registries), and T005/T005a (diagnostics/validation) can overlap. T006/T006a (wiring) must wait for registry interfaces.
- Within US1, task T009 (plugin extensions) can proceed once T007 begins, while T010 and T011 can run independently.
- US2 tasks T012–T016a mostly modify separate subsystems: T013 and T013a can run in parallel after T012 lands; T015 and T016/T016a are independent.
- US3 tasks T017–T021 allow T017, T018, and T019 to proceed in parallel once registries exist; T020 (plugin registration) depends on all three; T021 (tests) can start once T020 begins.

## Implementation Strategy

- **MVP Scope**: Complete Phases 1–3; this delivers plugin-only vector indexing with tests ensuring optionality.
- **Incremental Delivery**: After MVP, land Phase 4 for metadata/rebuild flows, then Phase 5 for query & BSON extensibility, validating each increment independently.
- **Testing Cadence**: Run `dotnet test LiteDB.Tests --filter "FullyQualifiedName~Vector"` and `dotnet test LiteDB.Vector.Tests` after each story phase; execute `scripts/verify-vector-clean.ps1` before final polish.
- **Performance Validation**: Run vector benchmark suite after T009 and T020 to verify ≤2% regression ceiling; regression gates Phase 5 completion.

