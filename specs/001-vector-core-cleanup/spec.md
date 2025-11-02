# Feature Specification: Vector Core Cleanup

**Feature Branch**: `001-vector-core-cleanup`  
**Created**: 2025-02-11  
**Status**: Draft  
**Input**: User description: "After `specs\001-vector-plugin-migration` - the core library `./LiteDB` still contains alot of `Vector` code. We want as little
- Find all occurences of `Vector`
- Write the result into the spec
- Decide wether to move it to `LiteDB.Vector` based on:
-- Is it really VectorSearch related or just happened to be named `Vector`
-- Can it be moved? If no, can we utilize the Plugin infrastructure to do so anyway? If no, can we improve the current plugin infrastructure to do so?"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Catalog Vector Debt (Priority: P1)

As a LiteDB maintainer, I need a verified inventory of all `Vector` references that still live in the core library so I can scope the remaining plugin migration work.

**Why this priority**: Without a complete inventory we cannot plan refactors nor guard against regressions when pruning vector logic.

**Independent Test**: Run `rg "Vector" LiteDB` and confirm every match appears in the inventory table with classification and recommendation.

**Acceptance Scenarios**:

1. **Given** the maintainer runs an automated search in `LiteDB/`, **When** they compare the results against the spec, **Then** every path and usage is documented with the same classification.
2. **Given** new vector-related files are added later, **When** the inventory is rerun, **Then** missing entries are treated as a failing test until the spec is updated.

---

### User Story 2 - Plan Plugin Migration (Priority: P2)

As the vector plugin owner, I want migration guidance for each vector-related component so I can move remaining logic into `LiteDB.Vector` without disrupting existing databases.

**Why this priority**: Targeted guidance lets the plugin absorb functionality while keeping the core binary lightweight.

**Independent Test**: Review the inventory and verify each row names either a plugin move path or rationale for staying in core, along with prerequisites.

**Acceptance Scenarios**:

1. **Given** an item marked “Move to plugin,” **When** the plugin team reviews the recommendation, **Then** they can outline implementation steps without discovering hidden dependencies.
2. **Given** an item marked “Requires infrastructure change,” **When** we inspect existing extension points, **Then** we can identify the missing hook and track it for future work.

---

### User Story 3 - Expose Infrastructure Gaps (Priority: P3)

As the extensibility architect, I need to understand where current plugin hooks fall short so we can design the next iteration of the plugin infrastructure.

**Why this priority**: Knowing the exact blockers prevents ad-hoc leaks of vector logic back into the core.

**Independent Test**: For each blocked migration item, verify the spec names the missing hook (e.g., custom page types, BSON extensions) and suggests upgrade scope.

**Acceptance Scenarios**:

1. **Given** a blocker is listed, **When** we map it to existing plugin registries, **Then** the gap is clearly outside today’s capabilities.
2. **Given** infrastructure updates are shipped later, **When** we revisit the spec, **Then** blocked items move into the “can migrate” category without re-auditing files.

---

### Edge Cases

- Databases created before the plugin is enabled already contain vector indexes; migration guidance must specify how to preserve data and rebuild indexes without core helpers.
- Applications that accidentally reference `Vector`-named symbols unrelated to similarity search must be filtered out of the inventory to avoid false positives (none were found in `LiteDB/`, but this guard remains on future audits).
- Environments loading LiteDB without the vector plugin must fail gracefully (clear errors, no hard dependency on `LiteDB.Vector` types) after the cleanup.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Document every remaining `Vector` occurrence under `LiteDB/` with file path, usage summary, and classification (vector search vs. incidental).
- **FR-002**: For each occurrence, define whether it can move to `LiteDB.Vector` now, needs plugin infrastructure support, or must remain in core with justification.
- **FR-003**: Highlight required changes to the plugin infrastructure that would enable migration for currently blocked items (e.g., custom BSON types, page management).
- **FR-004**: Provide a migration priority order so work can progress incrementally without breaking existing databases.
- **FR-005**: Outline verification steps (search, tests, compatibility checks) that prove vector logic no longer leaks from core once migration is executed.

#### Vector Code Inventory

Search executed on 2025-11-02 using `rg "Vector" LiteDB` produced **196 matches** spanning 28 unique files under `LiteDB/`. The table below captures every path, grouped by the five VectorComponentRecord documents in `specs/001-vector-core-cleanup/inventory/`.

| Area | Files | Vector Search Scope | Migration Decision | Notes |
|------|-------|---------------------|--------------------|-------|
| Public API surface | LiteDB/Engine/ILiteEngine.cs<br>LiteDB/Engine/Engine/Index.cs<br>LiteDB/Client/Shared/SharedEngine.cs<br>LiteDB/Client/Database/Collections/Index.cs<br>LiteDB/Client/Database/LiteRepository.cs<br>LiteDB/Client/Database/LiteQueryable.cs | Direct `EnsureVectorIndex` APIs, vector distance LINQ helpers, and repository wrappers expose vector search semantics to consumers. | Move to plugin (short term) | Replace bespoke methods with plugin-provided extension methods and index interceptors; ensure default core paths return clear errors when plugin absent. |
| Query planning & expressions | LiteDB/Engine/Query/Query.cs<br>LiteDB/Engine/Query/QueryOptimization.cs<br>LiteDB/Plugins/QueryPlanningContext.cs<br>LiteDB/Document/Expression/Parser/BsonExpressionType.cs | Stores vector filter state, planning flags, and dedicated expression node types (`VectorDist`, `VectorSim`). | Requires infrastructure change | Need plugin-owned query metadata bag and support for plugin-defined expression node kinds before removing these members from `Query` and planner. |
| BSON & serialization | LiteDB/Document/BsonType.cs<br>LiteDB/Document/BsonValue.cs<br>LiteDB/Document/BsonVector.cs<br>LiteDB/Document/Json/JsonWriter.cs<br>LiteDB/Utils/Extensions/BufferSliceExtensions.cs<br>LiteDB/Engine/Disk/Serializer/BufferReader.cs<br>LiteDB/Engine/Disk/Serializer/BufferWriter.cs | Adds `BsonType.Vector`, conversion helpers, JSON serialization, and binary encoding for float arrays. | Requires infrastructure change | Core currently has no extension point for custom BSON types; evaluate plugin-managed subtype registration or treat vectors as typed arrays to migrate these pieces. |
| Storage & engine metadata | LiteDB/Engine/Structures/VectorIndexNode.cs<br>LiteDB/Engine/Structures/VectorIndexMetadata.cs<br>LiteDB/Engine/Pages/VectorIndexPage.cs<br>LiteDB/Engine/Pages/BasePage.cs<br>LiteDB/Engine/Pages/CollectionPage.cs<br>LiteDB/Engine/FileReader/IndexInfo.cs<br>LiteDB/Engine/FileReader/FileReaderV8.cs<br>LiteDB/Engine/Services/SnapShot.cs<br>LiteDB/Engine/Engine/Rebuild.cs | Maintains vector index pages, metadata slots, file reader serialization, rebuild routines, and snapshot free-list management. | Requires infrastructure change | Plugin needs hooks to create custom page types, persist per-index metadata, and participate in rebuild/file-reader pipelines before these classes can move. |
| Service factory & internals | LiteDB/Engine/Services/VectorIndexServiceFactory.cs<br>LiteDB/Utils/Constants.cs | Factory wires plugin search service; friend assemblies grant plugin access to internals. | Move to plugin (short term) | Relocate factory to `LiteDB.Vector` and replace `InternalsVisibleTo` entries with targeted abstractions once storage hooks exist; keep friend access temporarily if engine internals remain required. |

#### Migration Priorities

1. **Priority 1 – Relocate `VectorIndexServiceFactory`** (`migration/priority-1-service-factory.md`)
   - **Decision**: `move-to-plugin-short` with Vector Plugin Team owning execution.
   - **Prerequisites**: Plugin extension methods must wrap EnsureVectorIndex entry points before the factory moves.
   - **Compatibility**: Must preserve existing vector indexes without rebuild by mirroring service discovery semantics.
   - **Performance**: Must maintain ≤2% regression from current throughput, validated against index build benchmarks.
   - **Fallback**: Core retains a guarded shim that raises clear guidance when the plugin is absent.
2. **Priority 2 – Deprecate core `EnsureVectorIndex` APIs** (`migration/priority-2-public-api.md`)
   - **Decision**: `move-to-plugin-short` covering public API surface adjustments.
   - **Prerequisites**: Plugin extension methods and documentation ready for consumer adoption.
   - **Compatibility**: Must preserve existing vector indexes without rebuild during the deprecation bridge.
   - **Performance**: Must maintain ≤2% regression from current throughput while both paths coexist.
   - **Fallback**: Provide user-facing messaging when vector APIs are called without the plugin installed.
3. **Priority 3 – Design query metadata extensions** (`migration/priority-3-query-metadata.md`)
   - **Decision**: `requires-infrastructure` hinging on a plugin-managed query metadata bag.
   - **Prerequisites**: Introduce metadata container through `QueryPlanningContext` and document serialization rules.
   - **Compatibility**: Must preserve existing vector indexes without rebuild by keeping planner outputs stable.
   - **Performance**: Must maintain ≤2% regression from current throughput despite metadata indirection.
   - **Fallback**: Supply an inert metadata implementation that blocks vector queries with actionable diagnostics.
4. **Priority 4 – Extend plugin for custom storage pages and BSON types** (`migration/priority-4-storage-bson.md`)
   - **Decision**: `requires-infrastructure` spanning BSON type registration and storage page factories.
   - **Prerequisites**: Add plugin-managed BSON registry and plugin-accessible page factory before relocating code.
   - **Compatibility**: Must preserve existing vector indexes without rebuild by honoring on-disk identifiers.
   - **Performance**: Must maintain ≤2% regression from current throughput across rebuild and serialization workloads.
   - **Fallback**: Keep compatibility layer that prompts users to enable the plugin before modifying legacy data.

#### Verification Strategy

- Re-run `rg "Vector" LiteDB` after each migration phase to ensure eliminated files no longer appear in core.
- Execute `dotnet test LiteDB.sln --settings tests.runsettings` plus vector plugin integration tests to confirm functionality survives the move.
- Open an existing database containing vector indexes without the plugin and verify the core surfaces explicit opt-in errors instead of silent failures.
- Validate plugin-only builds by removing `LiteDB.Vector` from dependencies and confirming the core compiles without vector code paths.

### Infrastructure Gaps

Current plugin hooks leave four blocking gaps that prevent vector code from leaving the core. Each entry is tracked under `specs/001-vector-core-cleanup/gaps/`.

| Gap | Description | Impact | Blocked Components | Proposed Solution | Backward Compatibility |
|-----|-------------|--------|--------------------|-------------------|------------------------|
| `gap-query-state` | Need plugin-owned metadata bag to replace `VectorField`, `VectorTarget`, `VectorMaxDistance`, `VectorMetric` on `Query`. | Critical | `inventory/query-planning.json` | Extend plugin query pipeline with a shared state bag or strongly typed accessor so vector plugins own planning metadata. | Keep legacy properties mapped to the bag until all callers migrate; surface clear errors when plugin absent. |
| `gap-bson-serialization` | Need plugin-managed BSON type registration so core no longer hardcodes `BsonType.Vector` or `BsonValue.AsVector`. | Critical | `inventory/bson-serialization.json` | Introduce reserved type code registration that lets plugins supply serializers/deserializers without touching core enums. | Provide shims that delegate to the plugin registry while older databases remain readable during rollout. |
| `gap-storage-pipeline` | Need plugin-accessible page factory/metadata APIs to host `VectorIndexPage`, metadata slots, rebuild, and file reader hooks. | Critical | `inventory/storage-engine.json` | Design a page factory and metadata extension API that lets plugins register custom pages and participate in rebuild/file-reader pipelines. | Preserve current page IDs and metadata layout until migration tooling rewrites existing databases. |
| `gap-indexing-extensibility` | Need full plugin strategy registration beyond `IIndexInterceptorRegistry` to replace `EnsureVectorIndex` behavior. | High | `inventory/public-api.json` | Extend interceptor registry so plugins register complete index strategies plus planning callbacks. | Keep existing EnsureVectorIndex shims forwarding to plugin strategies until consumers adopt plugin APIs. |

### Key Entities *(include if feature involves data)*

- **Vector Component Record**: Represents a grouped set of core files that still reference vector logic. Attributes: area name, file list, summary, migration decision, required infrastructure updates.
- **Migration Decision**: Classification assigned to each record (`Move to plugin`, `Requires infrastructure change`, `Remain in core`). Tracks prerequisites and owner team.
- **Plugin Infrastructure Gap**: Describes the missing extension point (e.g., custom BSON types, page factories) that blocks migration for a record.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Inventory completeness — running `rg "Vector" LiteDB` yields no matches outside the documented file paths once cleanup is executed.
- **SC-002**: Migration clarity — 100% of inventory rows specify a next action (move, upgrade hook, or retain) with rationale approved by both core and plugin maintainers.
- **SC-003**: Infrastructure roadmap — every “Requires infrastructure change” row maps to a tracked work item describing the extension point to implement.
- **SC-004**: Regression safety — after migration, core build/test matrix (`dotnet test LiteDB.sln --settings tests.runsettings` and plugin integration tests) passes without relying on the removed vector APIs.
