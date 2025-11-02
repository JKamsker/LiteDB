# Tasks: Vector Search Plugin Migration

**Branch**: `001-vector-plugin-migration` | **Date**: 2025-11-02  
**Input**: Design documents from `specs/001-vector-plugin-migration/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Feature Summary**: Migrate all vector search functionality from the LiteDB core library into the LiteDB.Vector extension package using the plugin infrastructure. This migration maintains full backward compatibility while enabling users to opt-in to vector search capabilities.

**Tests**: This specification does not explicitly request tests. Test tasks are omitted per speckit guidelines. Existing tests will be moved with the code during migration.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `- [ ] [ID] [P?] [Story?] Description with file path`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure for the LiteDB.Vector plugin package

- [ ] T001 Verify LiteDB.Vector project exists at `LiteDB.Vector/LiteDB.Vector.csproj` with netstandard2.0 and net8.0 targets
- [ ] T002 Verify VectorSearchPlugin.cs exists at `LiteDB.Vector/VectorSearchPlugin.cs` and implements ILitePlugin
- [ ] T003 Verify VectorIndexStrategy.cs exists at `LiteDB.Vector/VectorIndexStrategy.cs` and implements IIndexStrategy
- [ ] T004 Verify VectorExpressions.cs exists at `LiteDB.Vector/Expressions/VectorExpressions.cs`
- [ ] T005 Create folder structure: `LiteDB.Vector/Engine/`, `LiteDB.Vector/Query/`, `LiteDB.Vector/Extensions/`
- [ ] T006 Create LiteDB.Vector.Tests project at `LiteDB.Vector.Tests/LiteDB.Vector.Tests.csproj` with xUnit and FluentAssertions
- [ ] T007 [P] Enable XML documentation generation in LiteDB.Vector.csproj
- [ ] T008 [P] Enable nullable reference types in LiteDB.Vector.csproj

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete. These tasks ensure the plugin framework integration points are working.

- [ ] T009 Verify IIndexStrategy interface provides all necessary hooks: EnsureIndex, DropIndex, OnDocumentUpsert, OnDocumentDelete at `LiteDB/Plugins/IIndexStrategy.cs`
- [ ] T010 Verify ILitePlugin interface and plugin context at `LiteDB/Plugins/ILitePlugin.cs` and `LiteDB/Plugins/DefaultPluginContext.cs`
- [ ] T011 Verify core structures remain in LiteDB: BsonVector at `LiteDB/Document/BsonVector.cs`, VectorIndexMetadata at `LiteDB/Engine/Structures/VectorIndexMetadata.cs`, VectorIndexNode at `LiteDB/Engine/Structures/VectorIndexNode.cs`, VectorIndexPage at `LiteDB/Engine/Pages/VectorIndexPage.cs`
- [ ] T012 Run baseline benchmarks with existing vector implementation using `LiteDB.Benchmarks/Benchmarks/Queries/QueryWithVectorSimilarity.cs` to establish performance baseline
- [ ] T013 Document baseline performance metrics (index creation time, k-NN search time for k=1,10,100) for post-migration comparison

**Checkpoint**: Foundation verified - user story implementation can now begin

---

## Phase 3: User Story 1 - Existing Applications Continue Working (Priority: P1) 🎯 MVP

**Goal**: Applications using vector search features continue to work without any code changes after the migration. Users simply need to reference the vector extension package and enable it during database initialization.

**Independent Test**: Install the vector extension package, enable it when creating a database instance, and verify that existing vector index creation, insertion, querying, and deletion operations produce identical results to the current implementation.

**Why P1**: This is the most critical requirement because any breaking changes would prevent existing users from upgrading. The goal of this migration is architectural improvement without disrupting user workflows.

### Core Code Migration for User Story 1

- [ ] T014 [P] [US1] Move VectorDistanceMetric enum from `LiteDB/Client/Vector/VectorDistanceMetric.cs` to `LiteDB.Vector/VectorDistanceMetric.cs` (preserve namespace as LiteDB.Vector)
- [ ] T015 [P] [US1] Move VectorIndexOptions class from `LiteDB/Client/Vector/VectorIndexOptions.cs` to `LiteDB.Vector/VectorIndexOptions.cs` (preserve all constructor signatures)
- [ ] T016 [US1] Move VectorIndexService class from `LiteDB/Engine/Services/VectorIndexService.cs` to `LiteDB.Vector/Engine/VectorIndexService.cs` (preserve all HNSW algorithm logic including EfConstruction=24, DefaultEfSearch=32, MaxLevels=4, MaxNeighbors=8)
- [ ] T017 [US1] Update VectorIndexStrategy.cs at `LiteDB.Vector/VectorIndexStrategy.cs` to use the moved VectorIndexService for all index operations (EnsureIndex, DropIndex, OnDocumentUpsert, OnDocumentDelete)
- [ ] T018 [US1] Update VectorSearchPlugin.Initialize method at `LiteDB.Vector/VectorSearchPlugin.cs` to register VectorIndexStrategy with the moved types
- [ ] T019 [US1] Verify no references to moved types remain in LiteDB core project (grep for VectorIndexService, VectorIndexOptions, VectorDistanceMetric in LiteDB/ excluding Plugins/)
- [ ] T020 [US1] Remove moved source files from LiteDB core: delete `LiteDB/Engine/Services/VectorIndexService.cs`, `LiteDB/Client/Vector/VectorIndexOptions.cs`, `LiteDB/Client/Vector/VectorDistanceMetric.cs`

### Testing for User Story 1

- [ ] T021 [US1] Move test file from `LiteDB.Tests/Query/VectorIndex_Tests.cs` to `LiteDB.Vector.Tests/VectorIndex_Tests.cs` (preserve all test cases)
- [ ] T022 [US1] Update test project references: add LiteDB.Vector project reference to LiteDB.Vector.Tests.csproj
- [ ] T023 [US1] Run all moved tests in LiteDB.Vector.Tests to verify functionality: `dotnet test LiteDB.Vector.Tests/LiteDB.Vector.Tests.csproj`
- [ ] T024 [US1] Verify core structure tests remain in LiteDB.Tests: BsonVector_Tests.cs should stay in `LiteDB.Tests/BsonValue/`
- [ ] T025 [US1] Create new plugin integration test at `LiteDB.Vector.Tests/VectorPlugin_Tests.cs` to verify plugin registration and initialization
- [ ] T026 [US1] Validate backward compatibility: create test database with old implementation, open with new plugin-based implementation, verify indexes work

**Checkpoint**: At this point, User Story 1 should be fully functional - basic vector indexing works through the plugin

---

## Phase 4: User Story 2 - Vector Index Lifecycle Management (Priority: P1)

**Goal**: Developers can create, query, and manage vector indexes through the same APIs, with all operations handled by the extension infrastructure.

**Independent Test**: Create a vector index on a collection field, insert documents with vector embeddings, query using vector similarity search, and verify results match expected nearest neighbors. Drop the index and verify cleanup is complete.

**Why P1**: This ensures the core vector index functionality works correctly in the new architecture. It's essential for the feature to be usable.

### Query Infrastructure Migration for User Story 2

- [ ] T027 [US2] Move VectorIndexQuery class from `LiteDB/Engine/Query/IndexQuery/VectorIndexQuery.cs` to `LiteDB.Vector/Query/VectorIndexQuery.cs` (preserve query plan integration)
- [ ] T028 [US2] Update VectorIndexQuery to use the moved VectorIndexService for search operations in `LiteDB.Vector/Query/VectorIndexQuery.cs`
- [ ] T029 [US2] Verify query planner integration: ensure VectorIndexQuery is created for vector similarity conditions
- [ ] T030 [US2] Remove moved query file from LiteDB core: delete `LiteDB/Engine/Query/IndexQuery/VectorIndexQuery.cs`

### Index Lifecycle Implementation for User Story 2

- [ ] T031 [US2] Verify VectorIndexStrategy.EnsureIndex creates VectorIndexMetadata and builds HNSW graph at `LiteDB.Vector/VectorIndexStrategy.cs`
- [ ] T032 [US2] Verify VectorIndexStrategy.DropIndex removes all VectorIndexPage instances and frees resources at `LiteDB.Vector/VectorIndexStrategy.cs`
- [ ] T033 [US2] Verify VectorIndexStrategy.OnDocumentUpsert maintains index consistency when documents change at `LiteDB.Vector/VectorIndexStrategy.cs`
- [ ] T034 [US2] Verify VectorIndexStrategy.OnDocumentDelete removes nodes from graph structure at `LiteDB.Vector/VectorIndexStrategy.cs`

### Testing for User Story 2

- [ ] T035 [US2] Add lifecycle integration tests at `LiteDB.Vector.Tests/Integration/VectorIndexLifecycle_Tests.cs`: test create index, insert documents, verify index structure, drop index, verify cleanup
- [ ] T036 [US2] Add concurrent modification tests at `LiteDB.Vector.Tests/Integration/VectorIndexConcurrency_Tests.cs`: verify snapshot isolation during queries and updates
- [ ] T037 [US2] Verify all index operations complete within performance baseline: compare with metrics from T013

**Checkpoint**: At this point, User Stories 1 AND 2 should both work - full vector index lifecycle is functional

---

## Phase 5: User Story 3 - Distance Metric Support (Priority: P2)

**Goal**: Users can specify different distance metrics (Cosine, Euclidean, DotProduct) when creating vector indexes, and queries return appropriate distance/similarity scores.

**Independent Test**: Create three separate vector indexes using different metrics (Cosine, Euclidean, DotProduct), insert the same test data, query with the same vector, and verify that distance scores differ appropriately based on the mathematical properties of each metric.

**Why P2**: Different use cases require different distance metrics. This is important for flexibility but not blocking for basic functionality.

### Distance Metric Implementation for User Story 3

- [ ] T038 [US3] Verify VectorDistanceMetric enum includes all three metrics (Euclidean=0, Cosine=1, DotProduct=2) at `LiteDB.Vector/VectorDistanceMetric.cs`
- [ ] T039 [US3] Verify VectorIndexService.CalculateDistance implements all three metric formulas correctly at `LiteDB.Vector/Engine/VectorIndexService.cs`
- [ ] T040 [US3] Verify VectorIndexOptions defaults to Cosine metric at `LiteDB.Vector/VectorIndexOptions.cs`
- [ ] T041 [US3] Add connection string parsing for default metric in VectorSearchPlugin.Initialize at `LiteDB.Vector/VectorSearchPlugin.cs`: read "vector.metric" parameter

### Testing for User Story 3

- [ ] T042 [P] [US3] Add metric-specific tests at `LiteDB.Vector.Tests/VectorMetrics_Tests.cs`: verify Cosine returns values in [0,2], Euclidean returns values in [0,∞), DotProduct handles negative values
- [ ] T043 [P] [US3] Add cross-metric comparison tests at `LiteDB.Vector.Tests/VectorMetrics_Tests.cs`: same data with different metrics produces different rankings
- [ ] T044 [US3] Verify metric selection via VectorIndexOptions works correctly: create indexes with each metric, verify distance calculations match expected formulas

**Checkpoint**: All three distance metrics should work independently and produce correct results

---

## Phase 6: User Story 4 - Expression Function Registration (Priority: P2)

**Goal**: The vector similarity operator works correctly in query expressions for computing vector similarity at query time (outside of indexed searches).

**Independent Test**: Write a query using the vector similarity operator in a where clause or projection without using an index, and verify the similarity score is computed correctly using cosine similarity.

**Why P2**: This enables non-indexed vector comparisons in queries. Important for completeness but less critical than indexed search.

### Expression Function Implementation for User Story 4

- [ ] T045 [US4] Verify VectorExpressions.VectorSimilarity computes cosine distance correctly at `LiteDB.Vector/Expressions/VectorExpressions.cs`
- [ ] T046 [US4] Verify VectorSearchPlugin.Initialize registers VECTOR_SIM as both operator and function at `LiteDB.Vector/VectorSearchPlugin.cs`
- [ ] T047 [US4] Verify VECTOR_SIM returns BsonValue.Null for invalid inputs (dimension mismatch, non-vector types, NaN values) at `LiteDB.Vector/Expressions/VectorExpressions.cs`
- [ ] T048 [US4] Add XML documentation for VECTOR_SIM operator usage in `LiteDB.Vector/Expressions/VectorExpressions.cs`

### Testing for User Story 4

- [ ] T049 [P] [US4] Add expression tests at `LiteDB.Vector.Tests/VectorExpressions_Tests.cs`: verify VECTOR_SIM in WHERE clause filters correctly
- [ ] T050 [P] [US4] Add projection tests at `LiteDB.Vector.Tests/VectorExpressions_Tests.cs`: verify VECTOR_SIM in SELECT calculates distances
- [ ] T051 [US4] Add error handling tests at `LiteDB.Vector.Tests/VectorExpressions_Tests.cs`: verify Null returned for invalid inputs (dimension mismatch, non-arrays, etc.)

**Checkpoint**: Vector similarity operator should work in all query contexts (WHERE, SELECT, ORDER BY)

---

## Phase 7: User Story 5 - Extension Method Availability (Priority: P3)

**Goal**: Developers can use convenient extension methods for vector search operations on collections and queryables for a fluent API experience.

**Independent Test**: Call the vector search extension method to find top 10 nearest neighbors, then use queryable extension methods with LINQ operators and verify integration works correctly.

**Why P3**: These methods provide convenience and discoverability but are not essential since the same functionality is achievable through standard query APIs.

### Extension Method Migration for User Story 5

- [ ] T052 [P] [US5] Move LiteCollectionVectorExtensions from `LiteDB/Client/Vector/LiteCollectionVectorExtensions.cs` to `LiteDB.Vector/Extensions/LiteCollectionVectorExtensions.cs` (preserve all EnsureIndex overloads)
- [ ] T053 [P] [US5] Move LiteQueryableVectorExtensions from `LiteDB/Client/Vector/LiteQueryableVectorExtensions.cs` to `LiteDB.Vector/Extensions/LiteQueryableVectorExtensions.cs` (preserve all WhereNear and TopKNear overloads)
- [ ] T054 [P] [US5] Move LiteRepositoryVectorExtensions from `LiteDB/Client/Vector/LiteRepositoryVectorExtensions.cs` to `LiteDB.Vector/Extensions/LiteRepositoryVectorExtensions.cs` (preserve all EnsureIndex overloads)
- [ ] T055 [US5] Update all extension methods to use moved types (VectorIndexOptions, VectorDistanceMetric) in `LiteDB.Vector/Extensions/`
- [ ] T056 [US5] Add XML documentation to all public extension methods in `LiteDB.Vector/Extensions/` with usage examples
- [ ] T057 [US5] Remove moved extension files from LiteDB core: delete `LiteDB/Client/Vector/` folder

### Testing for User Story 5

- [ ] T058 [US5] Move extension method tests from `LiteDB.Tests/Query/VectorExtensionSurface_Tests.cs` to `LiteDB.Vector.Tests/VectorExtensions_Tests.cs`
- [ ] T059 [P] [US5] Add collection extension tests at `LiteDB.Vector.Tests/VectorExtensions_Tests.cs`: verify all EnsureIndex overloads (lambda, expression, named)
- [ ] T060 [P] [US5] Add queryable extension tests at `LiteDB.Vector.Tests/VectorExtensions_Tests.cs`: verify WhereNear and TopKNear with LINQ composition
- [ ] T061 [P] [US5] Add repository extension tests at `LiteDB.Vector.Tests/VectorExtensions_Tests.cs`: verify cross-collection index management
- [ ] T062 [US5] Add fluent API integration test at `LiteDB.Vector.Tests/Integration/FluentAPI_Tests.cs`: verify extension methods compose with Where, OrderBy, Limit, etc.

**Checkpoint**: All extension methods should provide convenient fluent API for vector operations

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories and finalize the migration

### Error Handling & Documentation

- [ ] T063 [P] Add clear error messages when plugin not registered: update relevant exception messages in `LiteDB.Vector/VectorIndexStrategy.cs` to guide users to install LiteDB.Vector package
- [ ] T064 [P] Verify all public APIs have XML documentation in `LiteDB.Vector/` with complete parameter descriptions and usage examples
- [ ] T065 [P] Update quickstart.md at `specs/001-vector-plugin-migration/quickstart.md` with any migration notes discovered during implementation
- [ ] T066 [P] Create migration guide document at `LiteDB.Vector/README.md` explaining plugin registration and upgrade steps

### Performance & Quality

- [ ] T067 Run post-migration benchmarks using `LiteDB.Benchmarks/Benchmarks/Queries/QueryWithVectorSimilarity.cs` and compare with baseline from T013
- [ ] T068 Verify performance variance is ≤5% compared to baseline (requirement from spec.md success criteria SC-003)
- [ ] T069 Run all tests in LiteDB.Vector.Tests: `dotnet test LiteDB.Vector.Tests/LiteDB.Vector.Tests.csproj`
- [ ] T070 Run all tests in LiteDB.Tests to verify core functionality unaffected: `dotnet test LiteDB.Tests/LiteDB.Tests.csproj`
- [ ] T071 Verify backward compatibility: test existing database files with vector indexes open and query correctly with plugin
- [ ] T072 Code cleanup: remove any commented-out code, unused using statements, and temporary debug logs in `LiteDB.Vector/`

### Build & Packaging

- [ ] T073 [P] Verify LiteDB.Vector.csproj generates NuGet package with correct metadata (package ID, version, description, authors)
- [ ] T074 [P] Verify LiteDB.Vector.csproj package includes XML documentation file for IntelliSense
- [ ] T075 Build LiteDB.Vector package: `dotnet pack LiteDB.Vector/LiteDB.Vector.csproj -c Release`
- [ ] T076 Verify package contents include all necessary assemblies (netstandard2.0, net8.0) and dependencies

### Validation

- [ ] T077 Run quickstart.md validation: follow all examples in `specs/001-vector-plugin-migration/quickstart.md` to verify they work with plugin-based implementation
- [ ] T078 Validate Constitution compliance: verify Principle VI (Plugin-First) satisfied, no violations of other principles
- [ ] T079 Final integration test: create new database, enable plugin, create vector index, insert documents, query, verify results match expectations

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup (Phase 1) - BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational (Phase 2) - Core migration
- **User Story 2 (Phase 4)**: Depends on User Story 1 (Phase 3) - Needs VectorIndexService moved
- **User Story 3 (Phase 5)**: Depends on User Story 1 (Phase 3) - Needs VectorDistanceMetric moved
- **User Story 4 (Phase 6)**: Depends on Foundational (Phase 2) - Independent of other stories
- **User Story 5 (Phase 7)**: Depends on User Story 1 (Phase 3) - Needs VectorIndexOptions moved
- **Polish (Phase 8)**: Depends on all user stories being complete

### User Story Dependencies

```
Foundational (Phase 2) - MUST complete first
    ↓
    ├─→ User Story 1 (P1) - Core Migration [T014-T026]
    │       ↓
    │       ├─→ User Story 2 (P1) - Query Infrastructure [T027-T037]
    │       ├─→ User Story 3 (P2) - Distance Metrics [T038-T044]
    │       └─→ User Story 5 (P3) - Extension Methods [T052-T062]
    │
    └─→ User Story 4 (P2) - Expression Functions [T045-T051] (Independent)
         
         ↓
    Polish (Phase 8) - Final validation [T063-T079]
```

### Critical Path (Sequential - Minimum Time)

1. **Phase 1: Setup** (T001-T008) - ~1 hour
2. **Phase 2: Foundational** (T009-T013) - ~2 hours
3. **Phase 3: User Story 1** (T014-T026) - ~8 hours (core migration)
4. **Phase 4: User Story 2** (T027-T037) - ~4 hours (query infrastructure)
5. **Phase 5: User Story 3** (T038-T044) - ~2 hours (metrics)
6. **Phase 6: User Story 4** (T045-T051) - ~2 hours (expressions)
7. **Phase 7: User Story 5** (T052-T062) - ~4 hours (extensions)
8. **Phase 8: Polish** (T063-T079) - ~4 hours (validation)

**Estimated Total (Sequential)**: ~27 hours (~3-4 days)

### Parallel Opportunities

#### Within Phase 1 (Setup)
- T007 and T008 can run in parallel (different config settings)

#### Within Phase 2 (Foundational)
All verification tasks can run in parallel if core is stable

#### Within Phase 3 (User Story 1)
- T014, T015 can run in parallel (different files)
- T021, T022 can run in parallel with T019, T020 (test vs code)

#### Within Phase 5 (User Story 3)
- T042 and T043 can run in parallel (different test files)

#### Within Phase 6 (User Story 4)
- T049, T050 can run in parallel (different test scenarios)

#### Within Phase 7 (User Story 5)
- T052, T053, T054 can run in parallel (different extension files)
- T059, T060, T061 can run in parallel (different test categories)

#### Within Phase 8 (Polish)
- T063, T064, T065, T066 can run in parallel (different documentation)
- T073, T074 can run in parallel (different package metadata)

---

## Parallel Execution Examples

### Example 1: Phase 1 Setup Tasks

```bash
# Launch in parallel (different configuration targets):
Task T007: "Enable XML documentation generation in LiteDB.Vector.csproj"
Task T008: "Enable nullable reference types in LiteDB.Vector.csproj"
```

### Example 2: User Story 1 Core Migration

```bash
# Launch in parallel (different source files):
Task T014: "Move VectorDistanceMetric enum"
Task T015: "Move VectorIndexOptions class"

# Then (depends on T014, T015):
Task T016: "Move VectorIndexService class"
```

### Example 3: User Story 5 Extension Methods

```bash
# Launch in parallel (different files):
Task T052: "Move LiteCollectionVectorExtensions"
Task T053: "Move LiteQueryableVectorExtensions"
Task T054: "Move LiteRepositoryVectorExtensions"

# Test in parallel:
Task T059: "Add collection extension tests"
Task T060: "Add queryable extension tests"
Task T061: "Add repository extension tests"
```

---

## Implementation Strategy

### MVP First (P1 User Stories Only)

**Goal**: Get basic vector search working through the plugin as quickly as possible

1. **Phase 1: Setup** (T001-T008) → ~1 hour
2. **Phase 2: Foundational** (T009-T013) → ~2 hours
3. **Phase 3: User Story 1** (T014-T026) → ~8 hours
4. **Phase 4: User Story 2** (T027-T037) → ~4 hours
5. **STOP and VALIDATE**: Run all tests, verify backward compatibility
6. **Deploy/Demo**: MVP ready - basic vector indexing works through plugin

**MVP Deliverable**: Core vector indexing functionality migrated to plugin with full backward compatibility

**Estimated Time**: ~15 hours (2 days)

### Incremental Delivery

1. **Foundation** (Phase 1-2) → ~3 hours  
   *Deliverable*: Project structure ready, baseline metrics captured

2. **MVP** (Phase 3-4) → +12 hours  
   *Deliverable*: Vector indexing works through plugin (US1 + US2)  
   *Demo*: Create index, insert documents, query - works identically

3. **Metrics** (Phase 5) → +2 hours  
   *Deliverable*: All three distance metrics supported (US3)  
   *Demo*: Same data, different metrics, different rankings

4. **Expressions** (Phase 6) → +2 hours  
   *Deliverable*: VECTOR_SIM works in all query contexts (US4)  
   *Demo*: Non-indexed similarity calculations in WHERE/SELECT

5. **Fluent API** (Phase 7) → +4 hours  
   *Deliverable*: Extension methods for convenient usage (US5)  
   *Demo*: Fluent API examples from quickstart.md

6. **Polish** (Phase 8) → +4 hours  
   *Deliverable*: Production-ready, documented, tested  
   *Demo*: Full quickstart validation, performance comparison

**Total Estimated Time**: ~27 hours (3-4 days)

### Parallel Team Strategy

With 3 developers after Foundational phase completes:

- **Developer A**: User Story 1 (Phase 3) - Core migration [T014-T026]
- **Developer B**: User Story 4 (Phase 6) - Expression functions [T045-T051] (independent)
- **Developer C**: Documentation and test infrastructure prep

After User Story 1 completes:
- **Developer A**: User Story 2 (Phase 4) - Query infrastructure [T027-T037]
- **Developer B**: User Story 3 (Phase 5) - Distance metrics [T038-T044]
- **Developer C**: User Story 5 (Phase 7) - Extension methods [T052-T062]

**Parallel Estimated Time**: ~12-15 hours (1.5-2 days with 3 developers)

---

## Validation Checklist

### Before Starting Implementation

- [ ] All design documents reviewed (plan.md, spec.md, data-model.md, contracts/)
- [ ] Plugin infrastructure verified in LiteDB core
- [ ] Baseline performance metrics captured
- [ ] Test project structure created

### After User Story 1 (MVP Checkpoint)

- [ ] Core types moved (VectorDistanceMetric, VectorIndexOptions, VectorIndexService)
- [ ] Plugin registration working
- [ ] Tests passing in LiteDB.Vector.Tests
- [ ] No vector code remains in LiteDB core (except structures)
- [ ] Backward compatibility verified with existing databases

### After All User Stories

- [ ] All extension methods moved and working
- [ ] All distance metrics supported
- [ ] Expression functions registered and tested
- [ ] Query infrastructure complete
- [ ] Performance ≤5% variance from baseline

### Before Merge to Main

- [ ] All tests passing (LiteDB.Tests + LiteDB.Vector.Tests)
- [ ] Quickstart.md examples validated
- [ ] NuGet package builds successfully
- [ ] Documentation complete and accurate
- [ ] Constitution compliance verified
- [ ] Code review completed

---

## Notes

- **[P] tasks** = different files, no dependencies, can run in parallel
- **[Story] labels** map tasks to specific user stories for traceability
- Each user story should be independently completable and testable
- All file paths are absolute from repository root
- Existing tests move with code (not rewritten)
- Performance baseline captured before migration for comparison
- Database file format unchanged - backward compatibility guaranteed
- Plugin registration required but no breaking API changes

---

## Success Criteria (from spec.md)

This task list addresses all success criteria:

- **SC-001**: All existing vector search tests pass - covered by test migration tasks (T021-T026, T035-T037, etc.)
- **SC-002**: Applications upgrade with minimal changes - covered by backward compatibility validation (T026, T071)
- **SC-003**: Performance ≤5% variance - covered by baseline (T012-T013) and post-migration benchmarks (T067-T068)
- **SC-004**: Database file compatibility - covered by structure verification (T011) and compatibility tests (T071)
- **SC-005**: Clear error messages without extension - covered by error handling (T063)
- **SC-006**: Test coverage maintained - covered by test migration (all US test tasks)
