# Tasks: Vector Core Cleanup

**Input**: Design documents from `.\specs\001-vector-core-cleanup\`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/openapi.yaml

**Tests**: Tests are NOT included in this task list as they were not explicitly requested in the feature specification. The focus is on documentation, inventory analysis, and planning work.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

This feature works with existing LiteDB solution structure:
- Core library: `.\LiteDB\`
- Plugin package: `.\LiteDB.Vector\`
- Documentation: `.\specs\001-vector-core-cleanup\`
- Tests: `.\LiteDB.Tests\` and `.\LiteDB.Vector.Tests\`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Initialize documentation structure and prepare inventory tooling

- [X] T001 Create branch `001-vector-core-cleanup` from master
- [X] T002 Verify feature documentation structure exists at `.\specs\001-vector-core-cleanup\`
- [X] T003 [P] Create inventory output directory `.\artifacts_temp\vector-cleanup\`
- [X] T004 [P] Install ripgrep (rg) tool if not already available for vector search operations

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Set up base inventory infrastructure and data structures that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T005 Create inventory data model schema definition document in `.\specs\001-vector-core-cleanup\inventory-schema.json`
- [X] T006 Create base VectorComponentRecord template in `.\specs\001-vector-core-cleanup\templates\component-record.json`
- [X] T007 Create base MigrationDecision template in `.\specs\001-vector-core-cleanup\templates\migration-decision.json`
- [X] T008 Create base PluginInfrastructureGap template in `.\specs\001-vector-core-cleanup\templates\infrastructure-gap.json`
- [X] T009 Create VerificationStep template in `.\specs\001-vector-core-cleanup\templates\verification-step.json`
- [X] T010 Validate existing project builds successfully with `dotnet build .\LiteDB.sln -c Release`

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Catalog Vector Debt (Priority: P1) 🎯 MVP

**Goal**: Create a complete, verified inventory of all Vector references in the LiteDB core library with classification and location tracking

**Independent Test**: Run `rg "Vector" .\LiteDB` and confirm every match appears in the inventory with classification

### Implementation for User Story 1

- [ ] T011 [US1] Execute comprehensive vector search: `rg "Vector" .\LiteDB > .\artifacts_temp\vector-cleanup\raw-search-results.txt`
- [ ] T012 [US1] Parse raw search results and extract unique file paths to `.\artifacts_temp\vector-cleanup\affected-files.txt`
- [ ] T013 [P] [US1] Create VectorComponentRecord for Public API surface area in `.\specs\001-vector-core-cleanup\inventory\public-api.json`
- [ ] T014 [P] [US1] Create VectorComponentRecord for Query planning & expressions area in `.\specs\001-vector-core-cleanup\inventory\query-planning.json`
- [ ] T015 [P] [US1] Create VectorComponentRecord for BSON & serialization area in `.\specs\001-vector-core-cleanup\inventory\bson-serialization.json`
- [ ] T016 [P] [US1] Create VectorComponentRecord for Storage & engine metadata area in `.\specs\001-vector-core-cleanup\inventory\storage-engine.json`
- [ ] T017 [P] [US1] Create VectorComponentRecord for Service factory & internals area in `.\specs\001-vector-core-cleanup\inventory\service-infrastructure.json`
- [ ] T018 [US1] Validate each VectorComponentRecord has all required fields (area, files, scopeSummary, decision reference)
- [ ] T019 [US1] Cross-check inventory files list against raw search results to ensure 100% coverage
- [ ] T020 [US1] Document Public API surface files in inventory: `LiteDB\Engine\ILiteEngine.cs`, `LiteDB\Engine\Engine\Index.cs`, `LiteDB\Client\Shared\SharedEngine.cs`, `LiteDB\Client\Database\Collections\Index.cs`, `LiteDB\Client\Database\LiteRepository.cs`, `LiteDB\Client\Database\LiteQueryable.cs`
- [ ] T021 [US1] Document Query planning files in inventory: `LiteDB\Engine\Query\Query.cs`, `LiteDB\Engine\Query\QueryOptimization.cs`, `LiteDB\Plugins\QueryPlanningContext.cs`, `LiteDB\Document\Expression\Parser\BsonExpressionType.cs`
- [ ] T022 [US1] Document BSON serialization files in inventory: `LiteDB\Document\BsonType.cs`, `LiteDB\Document\BsonValue.cs`, `LiteDB\Document\BsonVector.cs`, `LiteDB\Document\Json\JsonWriter.cs`, `LiteDB\Utils\Extensions\BufferSliceExtensions.cs`, `LiteDB\Engine\Disk\Serializer\BufferReader.cs`, `LiteDB\Engine\Disk\Serializer\BufferWriter.cs`
- [ ] T023 [US1] Document Storage engine files in inventory: `LiteDB\Engine\Structures\VectorIndexNode.cs`, `LiteDB\Engine\Structures\VectorIndexMetadata.cs`, `LiteDB\Engine\Pages\VectorIndexPage.cs`, `LiteDB\Engine\Pages\BasePage.cs`, `LiteDB\Engine\Pages\CollectionPage.cs`, `LiteDB\Engine\FileReader\IndexInfo.cs`, `LiteDB\Engine\FileReader\FileReaderV8.cs`, `LiteDB\Engine\Services\SnapShot.cs`, `LiteDB\Engine\Engine\Rebuild.cs`
- [ ] T024 [US1] Document Service infrastructure files in inventory: `LiteDB\Engine\Services\VectorIndexServiceFactory.cs`, `LiteDB\Utils\Constants.cs`
- [ ] T025 [US1] Add scope summary to Public API record: "Direct EnsureVectorIndex APIs, vector distance LINQ helpers, and repository wrappers expose vector search semantics to consumers"
- [ ] T026 [US1] Add scope summary to Query planning record: "Stores vector filter state, planning flags, and dedicated expression node types (VectorDist, VectorSim)"
- [ ] T027 [US1] Add scope summary to BSON serialization record: "Adds BsonType.Vector, conversion helpers, JSON serialization, and binary encoding for float arrays"
- [ ] T028 [US1] Add scope summary to Storage engine record: "Maintains vector index pages, metadata slots, file reader serialization, rebuild routines, and snapshot free-list management"
- [ ] T029 [US1] Add scope summary to Service infrastructure record: "Factory wires plugin search service; friend assemblies grant plugin access to internals"
- [ ] T030 [US1] Create master inventory index document in `.\specs\001-vector-core-cleanup\inventory\README.md`
- [ ] T031 [US1] Create VerificationStep for post-migration search validation in `.\specs\001-vector-core-cleanup\verification\search-validation.json`
- [ ] T032 [US1] Create VerificationStep for build validation in `.\specs\001-vector-core-cleanup\verification\build-validation.json`
- [ ] T033 [US1] Create VerificationStep for plugin compatibility check in `.\specs\001-vector-core-cleanup\verification\plugin-compatibility.json`
- [ ] T034 [US1] Create VerificationStep for existing database compatibility in `.\specs\001-vector-core-cleanup\verification\database-compatibility.json`
- [ ] T035 [US1] Update spec.md with final verified inventory table including all 196 matches from search results
- [ ] T036 [US1] Run final inventory verification: Execute `rg "Vector" .\LiteDB` and confirm output matches documented inventory

**Checkpoint**: At this point, User Story 1 should be complete with a verified inventory of all Vector references

---

## Phase 4: User Story 2 - Plan Plugin Migration (Priority: P2)

**Goal**: Provide detailed migration guidance for each vector component including prerequisites, blockers, and implementation steps

**Independent Test**: Review inventory and verify each component has a migration decision with clear rationale and next steps

### Implementation for User Story 2

- [ ] T037 [P] [US2] Create MigrationDecision "Move to Plugin (Short Term)" in `.\specs\001-vector-core-cleanup\decisions\move-to-plugin-short.json`
- [ ] T038 [P] [US2] Create MigrationDecision "Requires Infrastructure Change" in `.\specs\001-vector-core-cleanup\decisions\requires-infrastructure.json`
- [ ] T039 [P] [US2] Create MigrationDecision "Remain in Core" in `.\specs\001-vector-core-cleanup\decisions\remain-in-core.json`
- [ ] T040 [US2] Link Public API surface record to "Move to Plugin (Short Term)" decision with prerequisite: "Replace with plugin extension methods and index interceptors"
- [ ] T041 [US2] Link Service infrastructure record to "Move to Plugin (Short Term)" decision with prerequisite: "Relocate factory to LiteDB.Vector and replace InternalsVisibleTo"
- [ ] T042 [US2] Link Query planning record to "Requires Infrastructure Change" decision with prerequisite: "Design plugin-managed query metadata bag"
- [ ] T043 [US2] Link BSON serialization record to "Requires Infrastructure Change" decision with prerequisite: "Implement plugin-managed BSON type registration"
- [ ] T044 [US2] Link Storage engine record to "Requires Infrastructure Change" decision with prerequisite: "Design plugin-accessible page factory and metadata API"
- [ ] T045 [US2] Define migration priority 1: Relocate VectorIndexServiceFactory in `.\specs\001-vector-core-cleanup\migration\priority-1-service-factory.md`
- [ ] T046 [US2] Define migration priority 2: Deprecate core EnsureVectorIndex APIs in `.\specs\001-vector-core-cleanup\migration\priority-2-public-api.md`
- [ ] T047 [US2] Define migration priority 3: Design query metadata extensions in `.\specs\001-vector-core-cleanup\migration\priority-3-query-metadata.md`
- [ ] T048 [US2] Define migration priority 4: Extend plugin for custom pages and BSON types in `.\specs\001-vector-core-cleanup\migration\priority-4-storage-bson.md`
- [ ] T049 [US2] Document migration sequence for Public API surface with clear steps in migration plan priority-2-public-api.md
- [ ] T050 [US2] Document migration sequence for Service infrastructure with clear steps in migration plan priority-1-service-factory.md
- [ ] T051 [US2] Document migration prerequisites for Query planning components including plugin infrastructure requirements
- [ ] T052 [US2] Document migration prerequisites for BSON serialization including type registration design
- [ ] T053 [US2] Document migration prerequisites for Storage engine including page factory design
- [ ] T054 [US2] Add database compatibility notes to each migration plan: "Must preserve existing vector indexes without rebuild"
- [ ] T055 [US2] Add performance requirements to each migration plan: "Must maintain ≤2% regression from current throughput"
- [ ] T056 [US2] Create migration validation checklist in `.\specs\001-vector-core-cleanup\migration\validation-checklist.md`
- [ ] T057 [US2] Document fallback strategy for databases without plugin in each migration plan
- [ ] T058 [US2] Assign ownership to "Vector Plugin Team" for short-term migrations in decision files
- [ ] T059 [US2] Assign ownership to "Core Engine Team + Vector Plugin Team" for infrastructure-dependent migrations
- [ ] T060 [US2] Update spec.md with complete migration priorities section based on created plan documents

**Checkpoint**: At this point, User Stories 1 AND 2 should both be complete with inventory and migration plans

---

## Phase 5: User Story 3 - Expose Infrastructure Gaps (Priority: P3)

**Goal**: Document all missing plugin extension points that block migration so future infrastructure work can be planned

**Independent Test**: For each blocked migration, verify spec names the missing hook and upgrade scope

### Implementation for User Story 3

- [ ] T061 [P] [US3] Create PluginInfrastructureGap for Query State extensibility in `.\specs\001-vector-core-cleanup\gaps\query-state.json`
- [ ] T062 [P] [US3] Create PluginInfrastructureGap for BSON Serialization extensibility in `.\specs\001-vector-core-cleanup\gaps\bson-serialization.json`
- [ ] T063 [P] [US3] Create PluginInfrastructureGap for Storage Pipeline extensibility in `.\specs\001-vector-core-cleanup\gaps\storage-pipeline.json`
- [ ] T064 [P] [US3] Create PluginInfrastructureGap for Indexing extensibility in `.\specs\001-vector-core-cleanup\gaps\indexing.json`
- [ ] T065 [US3] Document Query State gap: "Need plugin-owned query metadata bag to replace VectorField, VectorTarget, VectorMaxDistance, VectorMetric in Query class"
- [ ] T066 [US3] Document BSON Serialization gap: "Need plugin-managed BSON type registration to replace hardcoded BsonType.Vector and BsonValue.AsVector"
- [ ] T067 [US3] Document Storage Pipeline gap: "Need plugin-accessible page factory/metadata API to support VectorIndexPage, VectorIndexMetadata, rebuild, and file reader participation"
- [ ] T068 [US3] Document Indexing gap: "Need complete plugin strategy registration for custom index types beyond current IIndexInterceptorRegistry"
- [ ] T069 [US3] Set impact level "Critical" for Query State gap (blocks query planning migration)
- [ ] T070 [US3] Set impact level "Critical" for BSON Serialization gap (blocks serialization migration)
- [ ] T071 [US3] Set impact level "Critical" for Storage Pipeline gap (blocks storage engine migration)
- [ ] T072 [US3] Set impact level "High" for Indexing gap (improves but doesn't block current interceptor approach)
- [ ] T073 [US3] Link Query State gap to Query planning component records in inventory
- [ ] T074 [US3] Link BSON Serialization gap to BSON serialization component records in inventory
- [ ] T075 [US3] Link Storage Pipeline gap to Storage engine component records in inventory
- [ ] T076 [US3] Link Indexing gap to Public API component records in inventory
- [ ] T077 [US3] Document proposed Query State solution from research.md: "Extend plugin system with query-state bag or strongly-typed accessor"
- [ ] T078 [US3] Document proposed BSON solution from research.md: "Introduce plugin-managed BSON type registration with reserved type codes"
- [ ] T079 [US3] Document proposed Storage solution from research.md: "Design plugin-accessible page factory and metadata extension API"
- [ ] T080 [US3] Document proposed Indexing solution from research.md: "Extend IIndexInterceptorRegistry for complete strategy registration"
- [ ] T081 [US3] Create infrastructure roadmap document in `.\specs\001-vector-core-cleanup\roadmap\infrastructure-roadmap.md`
- [ ] T082 [US3] Map each gap to estimated implementation scope (S/M/L) in roadmap document
- [ ] T083 [US3] Define acceptance criteria for Query State infrastructure upgrade
- [ ] T084 [US3] Define acceptance criteria for BSON Serialization infrastructure upgrade
- [ ] T085 [US3] Define acceptance criteria for Storage Pipeline infrastructure upgrade
- [ ] T086 [US3] Define acceptance criteria for Indexing infrastructure upgrade
- [ ] T087 [US3] Create gap tracking issue template in `.\specs\001-vector-core-cleanup\templates\gap-issue-template.md`
- [ ] T088 [US3] Document backward compatibility requirements for each infrastructure gap
- [ ] T089 [US3] Update spec.md with complete infrastructure gaps section including all 4 critical/high gaps

**Checkpoint**: All user stories should now be independently complete with inventory, migration plans, and infrastructure requirements

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, documentation updates, and cross-story verification

- [ ] T090 [P] Generate final inventory summary report in `.\specs\001-vector-core-cleanup\SUMMARY.md`
- [ ] T091 [P] Update research.md with any new findings or decision rationale discovered during task execution
- [ ] T092 [P] Create visualization of component dependencies in `.\specs\001-vector-core-cleanup\diagrams\component-dependencies.md`
- [ ] T093 Validate all JSON files are well-formed and match schema
- [ ] T094 Verify all file paths in inventory match actual repository structure
- [ ] T095 Run cross-story validation: Confirm each migration decision references valid gaps and components
- [ ] T096 Execute full verification suite from quickstart.md to validate inventory completeness
- [ ] T097 Update AGENTS.md with vector cleanup process documentation if applicable
- [ ] T098 Create executive summary of findings for core and plugin teams in `.\specs\001-vector-core-cleanup\EXECUTIVE-SUMMARY.md`
- [ ] T099 Document next steps and handoff to implementation teams in executive summary
- [ ] T100 Final validation: Run `rg "Vector" .\LiteDB` and confirm 100% match with documented inventory

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational (Phase 2) - Must complete before US2/US3
- **User Story 2 (Phase 4)**: Depends on US1 completion (needs inventory to create migration plans)
- **User Story 3 (Phase 5)**: Depends on US2 completion (needs migration decisions to identify gaps)
- **Polish (Phase 6)**: Depends on all user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Requires Foundational phase - Creates base inventory that US2 and US3 depend on
- **User Story 2 (P2)**: Requires US1 completion - Cannot create migration plans without complete inventory
- **User Story 3 (P3)**: Requires US2 completion - Infrastructure gaps emerge from migration blockers identified in US2

### Within Each User Story

**User Story 1:**
- Search and parse operations (T011-T012) before component record creation
- Component records (T013-T017) can be created in parallel
- Documentation tasks (T020-T024) can run in parallel after records exist
- Scope summaries (T025-T029) can run in parallel
- Final verification (T035-T036) must be last

**User Story 2:**
- Decision templates (T037-T039) can be created in parallel
- Linking decisions (T040-T044) must follow template creation
- Migration priorities (T045-T048) can be documented in parallel
- Documentation and ownership tasks can run in parallel after priorities defined
- Final spec update (T060) must be last

**User Story 3:**
- Gap creation (T061-T064) can run in parallel
- Gap documentation (T065-T068) can run in parallel after creation
- Impact levels (T069-T072) can be set in parallel
- Linking and solutions can proceed in parallel after gaps documented
- Final spec update (T089) must be last

### Parallel Opportunities

- **Phase 1**: All setup tasks can run in parallel (T002, T003, T004 are independent)
- **Phase 2**: Template creation tasks (T006-T009) can run in parallel
- **User Story 1**: Component records (T013-T017), file documentation (T020-T024), scope summaries (T025-T029), verification steps (T031-T034)
- **User Story 2**: Decision templates (T037-T039), migration priorities (T045-T048)
- **User Story 3**: Gap creation (T061-T064), gap documentation (T065-T068), impact levels (T069-T072), linking (T073-T076), solutions (T077-T080)
- **Phase 6**: Report generation, updates, and validation tasks (T090-T092) can run in parallel

---

## Parallel Example: User Story 1

```bash
# Launch all component record creation tasks together:
Task: "Create VectorComponentRecord for Public API surface in inventory/public-api.json"
Task: "Create VectorComponentRecord for Query planning in inventory/query-planning.json"
Task: "Create VectorComponentRecord for BSON serialization in inventory/bson-serialization.json"
Task: "Create VectorComponentRecord for Storage engine in inventory/storage-engine.json"
Task: "Create VectorComponentRecord for Service infrastructure in inventory/service-infrastructure.json"

# Launch all scope summary tasks together:
Task: "Add scope summary to Public API record"
Task: "Add scope summary to Query planning record"
Task: "Add scope summary to BSON serialization record"
Task: "Add scope summary to Storage engine record"
Task: "Add scope summary to Service infrastructure record"

# Launch all verification step creation together:
Task: "Create VerificationStep for search validation"
Task: "Create VerificationStep for build validation"
Task: "Create VerificationStep for plugin compatibility"
Task: "Create VerificationStep for database compatibility"
```

## Parallel Example: User Story 2

```bash
# Launch all decision template creation together:
Task: "Create MigrationDecision Move to Plugin (Short Term)"
Task: "Create MigrationDecision Requires Infrastructure Change"
Task: "Create MigrationDecision Remain in Core"

# Launch all migration priority documentation together:
Task: "Define migration priority 1: Service factory"
Task: "Define migration priority 2: Public API"
Task: "Define migration priority 3: Query metadata"
Task: "Define migration priority 4: Storage and BSON"
```

## Parallel Example: User Story 3

```bash
# Launch all infrastructure gap creation together:
Task: "Create PluginInfrastructureGap for Query State"
Task: "Create PluginInfrastructureGap for BSON Serialization"
Task: "Create PluginInfrastructureGap for Storage Pipeline"
Task: "Create PluginInfrastructureGap for Indexing"

# Launch all gap documentation together:
Task: "Document Query State gap details"
Task: "Document BSON Serialization gap details"
Task: "Document Storage Pipeline gap details"
Task: "Document Indexing gap details"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T004)
2. Complete Phase 2: Foundational (T005-T010) - CRITICAL foundation
3. Complete Phase 3: User Story 1 (T011-T036) - Complete inventory
4. **STOP and VALIDATE**: Run verification to confirm inventory is 100% complete
5. Share inventory with stakeholders for feedback

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 (T011-T036) → Validate inventory → Share results (MVP!)
3. Add User Story 2 (T037-T060) → Validate migration plans → Share guidance
4. Add User Story 3 (T061-T089) → Validate gaps → Share roadmap
5. Complete Polish (T090-T100) → Final validation → Deliver complete specification

Each story adds value:
- **After US1**: Teams know exact scope of vector debt
- **After US2**: Teams have actionable migration plans
- **After US3**: Teams understand infrastructure investments needed

### Parallel Team Strategy

With multiple developers (after Foundational phase completes):

1. **Sequential execution recommended** due to dependencies:
   - Developer A: User Story 1 (inventory) → Must complete first
   - Developer B: User Story 2 (migration plans) → Starts after US1
   - Developer C: User Story 3 (infrastructure gaps) → Starts after US2

2. **Within each story**, parallelize tasks:
   - US1: Multiple developers create component records simultaneously
   - US2: Multiple developers document migration priorities simultaneously
   - US3: Multiple developers document infrastructure gaps simultaneously

---

## Notes

- [P] tasks = different files, no dependencies, safe for parallel execution
- [Story] label maps task to specific user story (US1, US2, US3) for traceability
- Each user story depends on previous story's completion (sequential delivery)
- Within stories, many tasks can execute in parallel (component records, decisions, gaps)
- All file paths are absolute to avoid ambiguity
- Inventory must be 100% complete before creating migration plans
- Migration plans must exist before identifying infrastructure gaps
- This is primarily documentation and analysis work - no code changes to LiteDB core
- Focus is on planning and specification, not implementation
- Verification steps ensure inventory accuracy throughout the process
- Final deliverable is a complete migration roadmap, not migrated code
