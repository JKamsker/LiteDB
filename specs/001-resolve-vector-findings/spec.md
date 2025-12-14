# Feature Specification: Vector Findings Resolution

**Feature Branch**: `001-resolve-vector-findings`  
**Created**: 2025-11-02  
**Status**: Draft  
**Input**: User description: "Create a followup spec for addressing the findings in specs\001-vector-core-cleanup. At the end the issues must be resolved."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Deliver Vector Extensibility (Priority: P1)

As a core engine maintainer, I need plugin-owned extension points that cover query metadata, BSON typing, storage pages, and index strategy registration so vector capabilities can live entirely outside the LiteDB core without breaking existing databases.

**Why this priority**: Without these extensibility hooks, every downstream task in the cleanup backlog remains blocked and core binaries keep shipping vector code.

**Independent Test**: Enable a build that removes vector-specific fields from `LiteDB/` and verify vector CRUD queries still execute end-to-end when LiteDB.Vector registers the new extension points.

**Acceptance Scenarios**:

1. **Given** the core is built without vector-specific fields, **When** the LiteDB.Vector plugin is loaded, **Then** vector queries utilize plugin-provided metadata, page factories, BSON registration, and index strategies with all `gap-*` records marked resolved.
2. **Given** the plugin is absent, **When** an application invokes a former vector API, **Then** the core surfaces a deterministic opt-in error without accessing removed fields or crashing.

---

### User Story 2 - Migrate Vector Runtime to Plugin (Priority: P2)

As the vector plugin owner, I want the remaining vector runtime types, helpers, and migration shims moved from `LiteDB/` into `LiteDB.Vector` so the core artifact no longer includes vector-only behaviors.

**Why this priority**: Relocating the implementation closes the inventory from `specs/001-vector-core-cleanup`, letting the plugin release iterate independently while slimming the core footprint.

**Independent Test**: Run `rg "Vector" LiteDB` and confirm only documented compatibility/safety shims or neutral terminology remain, then execute plugin integration tests to prove all relocated code paths succeed.

**Acceptance Scenarios**:

1. **Given** the new extensibility model, **When** service factories, query helpers, and storage classes are rebuilt into LiteDB.Vector, **Then** the core solution compiles with only documented safety shims (including any temporary `InternalsVisibleTo` entries) and no broad vector runtime implementations remaining inside the core.
2. **Given** an upgraded application upgrades both core and plugin packages, **When** it rebuilds vector indexes or executes similarity search, **Then** the behavior matches pre-migration results with parity metrics recorded in the verification checklist.

---

### User Story 3 - Ship Upgrade & Compatibility Guardrails (Priority: P3)

As operations supporting existing LiteDB workloads, I need automated upgrade guidance, regression coverage, and diagnostics so deployments can adopt the plugin-backed implementation without data loss or silent feature loss.

**Why this priority**: Vector adopters must have a predictable migration path and validation story before we declare the cleanup finished.

**Independent Test**: Execute the verification suite (`dotnet test LiteDB.sln --settings tests.runsettings`, `dotnet test LiteDB.Vector.Tests`) plus the curated upgrade checklist and confirm no blocking regressions or unassigned work items remain.

**Acceptance Scenarios**:

1. **Given** a database created with legacy core vector code, **When** the upgrade tooling runs, **Then** the recorded migration steps convert the database to the plugin-backed layout with automated smoke tests passing afterward.
2. **Given** observability hooks are enabled, **When** deployments run without the plugin or with incompatible versions, **Then** administrators receive explicit telemetry and documentation references within a single deploy cycle.

---

### Edge Cases

- Upgrading deployments that still contain vector index pages while the plugin assembly fails to load must preserve read access and surface actionable remediation steps.
- Applications pinned to `netstandard2.0` must continue to compile; new extension points cannot rely on APIs missing from the legacy target.
- Mixed-version clusters (rolling upgrades) must allow nodes with old plugins to communicate safely with nodes running new extension points until the maintenance window completes.
- Single-process scenarios that never used vector capabilities must not incur measurable startup penalties or dependency bloat after the new infrastructure lands.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Provide a plugin-managed query metadata bag API and refactor `LiteDB.Engine.Query` consumers to rely on it, closing `gap-query-state` and removing core-owned vector planning fields.
- **FR-002**: Introduce a plugin-driven BSON type registration pipeline that deprecates hard-coded `BsonType.Vector` enums while maintaining backward-compatible readers until migration completes (`gap-bson-serialization` resolved).
- **FR-003**: Design and ship plugin-accessible page factories and metadata serializers that allow LiteDB.Vector to host vector index pages, rebuild routines, and snapshot flows without touching `LiteDB.Engine.Pages` (`gap-storage-pipeline` resolved).
- **FR-004**: Extend index extensibility so plugins register complete index strategies, replacing `EnsureVectorIndex` entry points with plugin-owned helpers and compatibility shims (`gap-indexing-extensibility` resolved).
- **FR-005**: Relocate remaining vector runtime files from `LiteDB/` to `LiteDB.Vector/`, leaving only documented compatibility and safety shims in core so databases remain consistent even when the plugin is absent, and update the inventory to reflect zero outstanding vector decisions.
- **FR-006**: Produce migration guidance, automation scripts, and verification steps that cover database upgrades, package updates, telemetry, and regression suites referenced in `specs/001-vector-core-cleanup/verification`.
- **FR-007**: Document observability and error-handling behaviors so deployments detect missing plugins or incompatible versions within one operational cycle, referencing the SUMMARY and roadmap artifacts.

## Assumptions

- Legacy constants such as `BsonType.Vector` may remain as thin aliases delegating to the plugin registry until a future major version removes them entirely.
- LiteDB.Vector will ship side-by-side with the core release so versioned breaking changes can be coordinated within a single release train.
- Existing `EnsureVectorIndex` APIs can be marked obsolete during this work; removing them entirely requires a separate deprecation decision after adoption metrics are reviewed.

### Key Entities *(include if feature involves data)*

- **QueryMetadataBag**: Plugin-registered structure that stores planner hints (target collection, distance metric, max distance) with serialization contracts for legacy property migration.
- **PluginCustomBsonTypeDescriptor**: Describes a reserved type code, serializer, deserializer, and compatibility shim that routes BSON vector payloads through the plugin pipeline.
- **CustomIndexStrategyDescriptor**: Aggregates page factory handles, rebuild hooks, and command interceptors that LiteDB.Vector registers to fulfil index operations without core dependencies.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Running `rg "Vector" LiteDB` after migration yields zero matches outside designated compatibility shims, and `specs/001-vector-core-cleanup/SUMMARY.md` reports no open components.
- **SC-002**: The verification suite plus plugin integration tests pass on both `netstandard2.0` and `net8.0` targets with vector workloads exercising the new extension points.
- **SC-003**: 100% of gap records (`gap-query-state`, `gap-bson-serialization`, `gap-storage-pipeline`, `gap-indexing-extensibility`) transition to a “Resolved” status with linked work items and approved owners.
- **SC-004**: Upgrade telemetry and documentation demonstrate that deployments without LiteDB.Vector detect the missing dependency and surface remediation steps within one operational cycle (24 hours).


