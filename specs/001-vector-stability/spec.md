# Feature Specification: Vector Stability Hardening

**Feature Branch**: `[001-vector-stability]`  
**Created**: 2025-11-02  
**Status**: Draft  
**Input**: User description: "Resolve dot-product threshold bug, fix legacy vector API messaging, scope plugin registries per database, and drop committed artifacts."

## User Scenarios & Testing *(mandatory)*

<!--
  IMPORTANT: User stories should be PRIORITIZED as user journeys ordered by importance.
  Each user story/journey must be INDEPENDENTLY TESTABLE - meaning if you implement just ONE of them,
  you should still have a viable MVP (Minimum Viable Product) that delivers value.
  
  Assign priorities (P1, P2, P3, etc.) to each story, where P1 is the most critical.
  Think of each story as a standalone slice of functionality that can be:
  - Developed independently
  - Tested independently
  - Deployed independently
  - Demonstrated to users independently
-->

### User Story 1 - Accurate Dot-Product Filtering (Priority: P1)

Operators executing dot-product vector searches need `maxDistance` (similarity threshold) honored consistently, regardless of whether it was set through LINQ helpers or injected via the new metadata bag. A regression test must first be authored and observed failing specifically because of the normalization bug before any production fixes ship.

**Why this priority**: Incorrect thresholds silently expand result sets, producing wrong answers and undermining vector search reliability.

**Independent Test**: Seed a dot-product index, run queries through both `LiteQueryableVectorExtensions` and metadata injection, and assert the filtered set never exceeds the expected results from the engine’s native metric implementation.

**Acceptance Scenarios**:

1. **Given** a regression test that reproduces the dot-product bug, **When** that test is run prior to the fix, **Then** it fails specifically because the threshold is not normalized.
2. **Given** the fix is applied, **When** that same regression test executes, **Then** it passes and only matches meeting or exceeding 0.25 similarity are returned.
3. **Given** metadata-provided thresholds, **When** the planner consumes the metadata, **Then** it normalizes negative comparisons the same way as the legacy path.

---

### User Story 2 - Remove Undeployed Legacy APIs (Priority: P2)

The obsolete `LiteQueryable.WhereNear/TopKNear` shims were never deployed. They should be fully removed to avoid maintaining dead compatibility code.

**Why this priority**: Eliminating unused APIs keeps the public surface lean, avoids confusing upgrade paths, and reduces maintenance.

**Independent Test**: Build and test suites must compile without the obsolete members; references in samples/tests should move to the plugin extensions.

**Acceptance Scenarios**:

1. **Given** the repository is rebuilt, **When** code searches for `WhereNear` or `TopKNear` methods on `LiteQueryable`, **Then** none exist.
2. **Given** tests relying on the old APIs are updated to use `LiteQueryableVectorExtensions`, **Then** the suite compiles and passes without relying on deleted symbols.

---

### User Story 3 - Multi-Database Plugin Isolation (Priority: P3)

Hosts often open multiple `LiteDatabase` instances, some with vector plugins and some without. They need each instance’s plugin registries to remain isolated so operations never interfere, ideally without relying on any global registry state at all.

**Why this priority**: The current global registry swap can cause random `PluginRequired` failures or crashes in long-lived processes.

**Independent Test**: Run concurrent operations against two databases (one vector-enabled, one not) and verify BSON serialization plus page creation succeed without cross-contamination.

**Acceptance Scenarios**:

1. **Given** two databases with different plugin contexts, **When** both perform reads/writes, **Then** each uses its own registry without overwriting the other and no global singleton state is toggled.
2. **Given** the engine starts without the vector plugin, **When** a vector-enabled database is later opened, **Then** it can establish registries without modifying global state shared with other databases.

---

### User Story 4 - Clean Repository State (Priority: P4)

Contributors want `artifacts_temp/*.db` build outputs to stay untracked so `git status` remains clean and reviews stay focused.

**Why this priority**: Tracked binary blobs bloat the repo and create merge conflicts without delivering value.

**Independent Test**: Delete database artifacts, run `git status`, and confirm no files under `artifacts_temp/` appear as tracked changes.

**Acceptance Scenarios**:

1. **Given** the ignore rules are updated, **When** vector upgrade scripts generate `.db` backups, **Then** they remain untracked.

### Edge Cases

- What happens when `maxDistance` is `NaN`, `Infinity`, or omitted? Ensure validation matches existing guardrails and the planner does not normalize invalid values.
- How does the system handle simultaneous disposal/creation of `LiteDatabaseServices` instances? Registry caches must handle lifetimes without leaking or leaving stale entries.
- What if artifact cleanup collides with users who intentionally checked in seed databases? Document opt-out or alternate locations.

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-001**: The query planner MUST normalize dot-product thresholds (including metadata-derived values) before comparing distances, and the regression test proving the bug MUST be written and observed failing before the fix is merged.
- **FR-002**: Vector metadata bags MUST persist enough information to reconstruct normalized thresholds without double-inversion across planner invocations.
- **FR-003**: The obsolete `LiteQueryable` vector APIs MUST be removed entirely from the public surface (they were never shipped).
- **FR-004**: `LiteDatabaseServices` and related resolver helpers MUST maintain per-context plugin registries so multiple databases can coexist safely without any global registry.
- **FR-005**: Repository tooling MUST ignore and remove committed `.db` artifacts under `artifacts_temp/`, preventing them from reappearing in `git status`.
- **FR-006**: Automated tests MUST cover dot-product enforcement (including pre-fix regression), removal of legacy APIs, and multi-database isolation to prevent regressions.

### Key Entities *(include if feature involves data)*

- **QueryMetadataBag**: Holds per-plugin query metadata, including vector field, target, metric, and (normalized) max-distance data.
- **Plugin Registry Cache**: Maps `ILitePluginContext` instances to their resolver infrastructure (BSON types, page factories, vector strategies) without global mutation.

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: Dot-product regression test fails pre-fix and passes post-fix, ensuring the normalization bug cannot return undetected.
- **SC-002**: Build/test runs contain zero references to the removed `LiteQueryable` vector APIs (compile-time enforcement).
- **SC-003**: Running dual databases (with/without plugins) for 10k operations completes without `PluginRequired`, resolver mismatch exceptions, or global registry mutations.
- **SC-004**: `git status` after running `scripts/vector/Invoke-VectorUpgrade.ps1` shows zero tracked files under `artifacts_temp/`.
