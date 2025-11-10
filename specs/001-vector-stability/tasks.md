# Tasks: Vector Stability Hardening

**Input**: Design documents from /specs/001-vector-stability/
**Prerequisites**: plan.md, spec.md

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Populate regression summary in specs/001-vector-stability/research.md covering dot-product, legacy API removal, plugin scope, and artifact cleanup contexts.
- [X] T002 Document end-to-end verification steps (dotnet test targets + git commands) in specs/001-vector-stability/quickstart.md to guide repetitive validation (include "fail first" instructions for US1).

---

## Phase 2: Foundational (Blocking Prerequisites)

- [X] T003 Create a reusable vector test fixture helper in LiteDB.Vector.Tests/Infrastructure/VectorTestContext.cs for building indexes and executing similarity queries across stories.
- [X] T004 [P] Extend LiteDB.Tests/Utils/DatabaseFactory.cs to spin up multiple LiteDatabase instances simultaneously for upcoming isolation tests.

---

## Phase 3: User Story 1 - Accurate Dot-Product Filtering (Priority: P1) – **MVP**

**Goal**: Normalize dot-product thresholds across metadata and planner flows, beginning with a failing regression test.

**Independent Test**: The new regression test in LiteDB.Vector.Tests/VectorIndex_Tests.cs fails on the base code and passes after the fix.

- [X] T005 [P] [US1] Add a regression test case in LiteDB.Vector.Tests/VectorIndex_Tests.cs that reproduces the incorrect dot-product maxDistance behavior via both metadata and LINQ entry points.
- [X] T006 [US1] Run the new regression test against current code, capture the failing output in specs/001-vector-stability/quickstart.md, and gate further work until the failure is confirmed.
- [X] T007 [US1] Introduce a shared normalization helper (e.g., NormalizeMaxDistance) in LiteDB.Vector/Utils/VectorEnsure.cs for dot-product handling.
- [X] T008 [US1] Update LiteDB/Engine/Query/Query.cs metadata setters/getters to store normalized maxDistance values and version them safely.
- [X] T009 [US1] Adjust LiteDB.Vector/Query/VectorIndexPlanningRule.cs and LiteDB.Vector/Extensions/QueryableExtensions.cs to consume the helper for both metadata and legacy flows.
- [X] T010 [US1] Ensure LiteDB.Vector/Extensions/VectorScoreQueryableResult.cs reads normalized thresholds to keep projection filters aligned.
- [X] T011 [US1] Extend LiteDB.Tests/Plugins/QueryMetadataBagTests.cs to verify persisted max-distance values remain normalized and version-compatible.

---

## Phase 4: User Story 2 - Remove Undeployed Legacy APIs (Priority: P2)

**Goal**: Delete the obsolete LiteQueryable vector APIs that never shipped.

**Independent Test**: Build/test runs succeed without any LiteQueryable.WhereNear/TopKNear members in the tree.

- [X] T012 [US2] Remove the obsolete vector methods (and XML docs) from LiteDB/Client/Database/LiteQueryable.cs, including their backing fields/helpers.
- [X] T013 [US2] Update callers/tests to use LiteQueryableVectorExtensions exclusively; delete LiteQueryable-specific tests under LiteDB.Tests/Client/ that referenced the removed APIs.
- [X] T014 [US2] Sweep docs (docs/plugins/plugin-development.md, README snippets) to ensure no references remain to the deleted APIs.

---

## Phase 5: User Story 3 - Multi-Database Plugin Isolation (Priority: P3)

**Goal**: Remove global plugin registries so each LiteDatabase instance maintains its own independent context.

**Independent Test**: Engine tests should open vector-enabled and vanilla databases concurrently without registry collisions or shared state writes.

- [X] T015 [US3] Refactor LiteDB/Document/BsonType.cs (including BsonTypeResolver) to eliminate global singletons and cache registries strictly per ILitePluginContext.
- [X] T016 [US3] Rework LiteDB/Engine/Pages/PageFactoryRegistry.cs and LiteDB/Client/Database/LiteDatabaseServices.cs to obtain registries purely from the owning context without mutating static state.
- [X] T017 [P] [US3] Update LiteDB/Plugins/DefaultPluginContext.cs plus related interfaces to expose thread-safe factory/type registries that can be instantiated per database.
- [ ] T018 [US3] Add an isolation test in LiteDB.Tests/Engine/PageFactoryRegistry_Tests.cs proving two contexts retain distinct page factory registrations when run in parallel.
- [ ] T019 [P] [US3] Add a BSON-type isolation test (two simultaneous LiteDatabase instances) in LiteDB.Tests/Engine/Plugins/PluginAbsentTests.cs (or a new fixture) to ensure vector serialization remains available only when the plugin is loaded.

---

## Phase 6: User Story 4 - Clean Repository State (Priority: P4)

**Goal**: Remove committed artifacts and enforce ignore rules for vector upgrade outputs.

**Independent Test**: git status after running scripts/vector/Invoke-VectorUpgrade.ps1 shows no tracked changes.

- [ ] T020 [US4] Delete the existing rtifacts_temp/vector-followup/*.db files and extend .gitignore to exclude future .db/backup outputs under rtifacts_temp/.
- [ ] T021 [P] [US4] Update scripts/vector/Invoke-VectorUpgrade.ps1 (and related helper scripts) to ensure generated databases land under ignored folders or temp directories.
- [ ] T022 [US4] Document the artifact-cleanup expectation in docs/plugins/plugin-development.md so contributors reroute upgrade outputs locally.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T023 Run the focused verification script from specs/001-vector-stability/quickstart.md (dotnet test suites + git status) and attach results to the PR.

---

## Dependencies & Execution Order

1. **Setup (Phase 1)** → capture research + quickstart guidance.
2. **Foundational (Phase 2)** → provide shared fixtures/harness upgrades. Blocks all user stories.
3. **User Story Phases** proceed in priority order (US1 → US2 → US3 → US4). Each story is independently testable once its tasks finish.
4. **Polish Phase** runs after all targeted stories are complete.

## Parallel Opportunities

- T004, T005, T007, T010, T013, T017, T019, and T021 operate on distinct files and can run concurrently once their prerequisites finish.
- Different user stories (US2–US4) can start after Foundational work if staffed separately, as they touch disjoint areas of the codebase.

## Implementation Strategy

- **MVP**: Complete US1 (regression test + fix), then pause for validation.
- **Incremental Delivery**: Layer US2 (API removal) and US3 (registry isolation) sequentially, keeping each story shippable. Finish with US4 hygiene changes.
- **Testing Cadence**: After each user story, execute the relevant subset from specs/001-vector-stability/quickstart.md to confirm independent functionality.
