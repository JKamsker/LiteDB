# Feature Specification: Vector Plugin Isolation

**Feature Branch**: `[001-vector-plugin-extraction]`  
**Created**: 2025-11-16  
**Status**: Draft  
**Input**: User description: "Move all remaining vector search capabilities (BSON types, index metadata, query hooks, APIs) out of the core LiteDB library and into the LiteDB.Vector plugin."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Plugin-Only Vector Indexing (Priority: P1)

As a LiteDB developer who references only the base package, I need vector-specific APIs, enums, and diagnostics to exist solely inside LiteDB.Vector so my application remains free of vector dependencies until I install the plugin.

**Why this priority**: Ensures the architectural boundary goal—vector functionality is optional and does not leak into the core assembly.

**Independent Test**: Build and inspect a project referencing only LiteDB; no `Vector*` symbols appear. Add LiteDB.Vector and run `LiteCollectionVectorExtensions.EnsureIndex(...)`; functionality works end-to-end via plugin-registered services.

**Acceptance Scenarios**:

1. **Given** an app referencing only LiteDB, **When** compiling and reflecting public APIs, **Then** no vector-related types or methods are exposed.
2. **Given** an app with LiteDB.Vector installed, **When** invoking vector index extension methods, **Then** the plugin provides all required services without accessing LiteDB internals.

---

### User Story 2 - Pluggable Storage Metadata (Priority: P2)

As an engine maintainer, I want page factories, index metadata serializers, rebuild/import hooks, and diagnostics to treat vector data as generic plugin payloads so future plugins can reuse the same extensibility points.

**Why this priority**: Prevents future coupling and lets other custom index/page types adopt the same mechanism.

**Independent Test**: Run rebuild/import with LiteDB.Vector present-metadata read/write flows exclusively through plugin serializers. Remove the plugin-core emits a generic "custom strategy missing" error without referencing vector types.

**Implementation Notes**:

- Collection pages will expose a plugin-agnostic metadata bag rather than a `VectorIndexMetadataSerializer` hook. Each entry records `{ pluginId, payload }`, keeping the payload opaque until the owning plugin registers a serializer. Core surfaces the pluginId to diagnostics and refuses to open that index when the plugin is missing, but other indexes/collections remain available.
- Page types follow the same pattern: plugins reserve page type identifiers (e.g., `"VectorIndex"`) via the page factory registry. Persisted pages store the numeric code plus pluginId so core can reject unsupported page types deterministically instead of hardcoding `PageType.VectorIndex`.

**Acceptance Scenarios**:

1. **Given** LiteDB.Vector registers its metadata serializer, **When** rebuilding a database, **Then** core delegates metadata operations through the serializer interface without referencing vector classes.
2. **Given** the plugin is absent, **When** pre-release prototype files (produced during development builds) contain vector metadata, **Then** core aborts with a plugin-agnostic diagnostic indicating no strategy is registered for that custom index type.

---

### User Story 3 - Query & BSON Extensibility (Priority: P3)

As a plugin author, I need the query planner and BSON serializer to accept dynamically registered operators and types so LiteDB.Vector (and future plugins) can add tokens like `VECTOR_DIST` and custom BSON encodings without modifying the core parser or serializer.

**Why this priority**: Guarantees long-term extensibility and keeps expression/BSON stacks slim.

**Independent Test**: Launch LiteDB shell without the plugin—vector operators/types unavailable. Install the plugin—operators and vector BSON type appear because the plugin registers them at startup.

**Acceptance Scenarios**:

1. **Given** LiteDB runs without LiteDB.Vector, **When** parsing `VECTOR_DIST`, **Then** the expression registry reports “unknown operator” with no baked-in vector branches.
2. **Given** LiteDB.Vector registers its BSON handler, **When** serializing a document containing vector data, **Then** serialization succeeds via plugin dispatch.

---

### Edge Cases

- What happens when an older database contains vector pages but LiteDB.Vector is not loaded? Core must keep LiteDB functional by either refusing to open the database, isolating only affected collections/documents, or allowing read-only access when stability is not impacted, and the chosen behavior must be deterministic and documented.
- When LiteDB.Vector is absent yet plugin-owned assets are detected, LiteDB MUST follow host-controlled missing-plugin policy (`PluginMissingBehavior`, default strict) and keep unaffected collections usable. Warnings are emitted only in non-strict modes. Any operation that touches plugin-owned indexes/pages/BSON types MUST throw the standard `VectorCompatibility.PluginRequired` exception (with diagnostics) rather than silently succeeding.
- How does the system behave if multiple plugins register different custom page types or BSON codes that overlap? Need deterministic precedence rules and conflict detection surfaced via plugin diagnostics.
- How are upgrade/downgrade scenarios handled when vector metadata versions change? Plugin must negotiate metadata schema or fail gracefully with actionable guidance.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Core LiteDB assemblies MUST expose vector functionality only through neutral extension points; no public `Vector*` members or enum values remain once the plugin is removed.
- **FR-002**: LiteDB.Vector MUST provide end-to-end vector index creation, drop, diagnostics, and query planning by registering services through official plugin registries, while any temporary `InternalsVisibleTo` declarations remain tightly scoped and validated so LiteDB core exposes, ideally little to no, vector APIs.
- **FR-003**: Rebuild/import flows MUST serialize vector metadata exclusively via plugin-provided descriptors registered in `IPluginIndexMetadataRegistry` (e.g., `PluginIndexMetadataDescriptor`) and refuse to operate on metadata whose serializer/descriptor is absent (unless an explicit salvage option is enabled).
- **FR-004**: Query planning and expression parsing MUST rely on plugin-registered SQL functions/operators and planner cost hooks so tokens like `VECTOR_SIM`, `VECTOR_DIST`, or `VECTOR_KNN` exist only when LiteDB.Vector enables them and the planner only considers vector paths when the plugin registers them.
- **FR-005**: BSON serialization MUST allow plugin-defined type codes/handlers, enabling LiteDB.Vector to inject its vector type implementation without core awareness.
- **FR-006**: Diagnostics for missing plugin functionality MUST include the plugin ID (e.g., `LiteDB.Vector`) in user-facing error messages to reduce support overhead while routing through generic plugin-missing pathways (per research decision #1).
- **FR-007**: Documentation and samples MUST describe vector capabilities as an optional plugin feature, pointing users to LiteDB.Vector installation and usage steps.
- **FR-008**: LiteDB MUST remain fully functional without LiteDB.Vector; when opening databases created with vector search enabled, the engine MUST follow one of the documented behaviors: (a) refuse to open the database up front, (b) refuse to open only documents/collections that require the missing plugin while keeping the rest stable, or (c) allow the database to open if the absence does not impact stability, and the system MUST log which path was chosen.
- **FR-009**: Core MUST replace vector-specific metadata structures with generic plugin registries: collection pages store plugin-owned metadata blobs annotated with `pluginId`, rebuild/import uses plugin-provided serializers for any custom index metadata, and the page factory registry treats plugin-reserved page types uniformly so no `Vector*` enums or helpers remain in LiteDB once the plugin is removed. Reserved identifier ranges (e.g., BSON type codes `0x90–0x9F`, page codes `0xE0–0xEF`) MUST be documented and validated so conflicts are rejected at build time.
- **FR-010**: Core MUST NOT parse or upgrade prerelease vector formats. If plugin-owned assets (indexes, BSON types, page codes) are encountered without the owning plugin, behavior follows host-controlled `PluginMissingBehavior` (default strict): affected operations MUST fail with `LITE2002` (including the `PluginId`, collection/index identifiers, and remediation) while non-vector data stays accessible; warnings are emitted only in non-strict modes.

### Key Entities *(include if feature involves data)*

- **CustomIndexStrategy**: Represents plugin-defined index logic (ensure/drop delegates, metadata serializer reference, diagnostic metadata).
- **CustomBsonTypeDescriptor**: Holds plugin-registered BSON type code, serializer/deserializer delegates, and JSON formatter behavior.
- **PluginPageFactory**: Describes plugin-owned page types with logical names, numeric codes, and constructors used by storage services.
- **QueryOperatorRegistration / PlannerCostHook**: Registry entries that allow plugins to add SQL functions/operators (`VECTOR_DIST`, `VECTOR_KNN`, etc.) plus planner rule/cost contributions so vector plans exist only when the plugin registers them.
- **Reserved Identifier Ranges**: LiteDB core documents reserved BSON type codes (e.g., `0x90–0x9F`) and page type codes (e.g., `0xE0–0xEF`) for LiteDB.Vector; plugins must declare their codes during registration so conflicts are rejected.

### Storage Identifiers (final)

- **BSON type – Vector**: `0x90` (plugin-owned; no legacy aliases remain in core).
- **Page types – Vector storage**: `0xE0–0xE3` reserved for LiteDB.Vector (current release uses `0xE0` for `VectorIndexPage`).
- **IndexKind – HNSW**: `"vector.hnsw"` for metadata descriptors and strategy registration.
- Core validates these IDs so other plugins cannot collide. Unknown plugin IDs encountered without the plugin follow the behavior matrix defined below.

### Extensibility Interfaces *(new design details)*

| Interface/Type | Namespace | Description |
|----------------|-----------|-------------|
| `IPluginIndexMetadataRegistry` + `PluginIndexMetadataDescriptor` | `LiteDB.Plugins.Indexing` | Core exposes a registry that lets plugins register metadata serializers. Each descriptor defines `PluginId`, `string IndexKind`, `Func<BsonDocument, byte[]> Serialize`, and `Func<byte[], BsonDocument> Deserialize`. Collection pages persist opaque metadata payloads attributed to `pluginId`; plugins deserialize those payloads into metadata documents used for diagnostics/planning/rebuild. LiteDB.Vector registers a descriptor for `IndexKind = "vector.hnsw"` (or similar) that understands the slot/dimension/metric payload. |
| `IPageTypeRegistry` + extended `PageFactoryRegistration` | `LiteDB.Plugins.Storage` | Builds on the current page factory registry by adding explicit page-type declarations: plugins call `context.RegisterPageFactory(new PageFactoryRegistration(pluginId: "LiteDB.Vector", pageType: "VectorIndex", numericCode: 0xE0, compatibilityRange: ">=8.0", factory: ...))`. Persisted pages record the numeric code; decoding an unregistered plugin-owned page type must throw `VectorCompatibility.PluginRequired` (`LITE2002`) when accessed (warnings only in non-strict modes). Reserved numeric ranges prevent overlaps. |
| `ICustomBsonTypeRegistry` + `CustomBsonTypeDescriptor` | `LiteDB.Plugins.Bson` | Plugins reserve BSON type codes using `context.RegisterBsonType(new CustomBsonTypeDescriptor(pluginId, typeCode, name, serializer, deserializer))`. LiteDB.Vector supplies the handler using a new plugin-owned type code (e.g., `0x90`) so BSON serialization/deserialization flows through the plugin with no legacy aliases in core. Core keeps `LiteDB.Core` registrations for built-in types but no longer contains vector-specific serializers. |
| `ISqlFunctionRegistry` / `IQueryOperatorRegistry` / `IQueryCostModelRegistry` | `LiteDB.Plugins.Query` | New registries that allow plugins to add SQL functions/operators plus cost model hooks. LiteDB.Vector uses them to register `VECTOR_DIST`, `VECTOR_SIM`, `VECTOR_KNN`, etc., and to advertise planner rules and costs only when the plugin is loaded. |

**LiteDB.Vector Migration Path**

1. During `VectorSearchPlugin.Initialize`, register the BSON handler (new plugin-owned type code), metadata descriptor (`IndexKind = "vector.hnsw"`), SQL operators/cost hooks, and page factory (`pageType = "VectorIndex"`, code `0xE0`) before hooking up query/index services.
2. When the plugin is absent, collection pages still contain `{pluginId="LiteDB.Vector", payload=vector blob}` records. Behavior follows host-controlled `PluginMissingBehavior` (default strict): unaffected collections remain usable, and operations that touch vector-owned indexes/pages/BSON types fail with standardized `LITE2002` diagnostics.
3. Rebuild/import/FileReader consult `IPluginIndexMetadataRegistry` to serialize/deserialize plugin-owned metadata. If no serializer exists, they abort with `LITE2002` by default; only an explicit salvage option may drop the affected index (with a durable report).
4. LiteDB.Vector exposes a helper (for example, `VectorMigrate.RebuildAll`) that applications can call to drop prerelease indexes and recreate them using the GA plugin once it is installed.

### Behavior Matrix (authoritative)

| Scenario | Safe To Ignore? | Plugin Loaded? | Outcome |
|----------|----------------:|:--------------:|---------|
| Prerelease vector artifacts detected | Yes | No | Database opens. Non-vector reads/writes allowed. Vector operations fail with `VectorCompatibility.LegacyIndexNeedsRebuild (LITE2002)` and remediation text; warnings are emitted only in non-strict modes. |
| Prerelease vector artifacts detected | No | No | Database refuses to open with `VectorCompatibility.LegacyIndexNeedsRebuild (LITE2002)` because the artifacts are unsafe to ignore. |
| GA vector data present | — | No | Database opens. Behavior follows host-controlled `PluginMissingBehavior` (default strict): affected collections are refused or read-only, and any writes/DDL that would touch them (including Ensure/DropIndex, Rebuild, Drop/RenameCollection) fail with `VectorCompatibility.PluginRequired (LITE2002)` naming `LiteDB.Vector`; warnings are emitted only in non-strict modes. |
| GA vector data present | — | Yes | Full functionality (BSON, operators, planner, diagnostics) is available. |
| No vector data present | — | Yes/No | Standard LiteDB behavior. |

**Compaction & Shrink**: If a compaction/shrink would rewrite plugin-owned pages and the plugin is missing, refuse the operation with `VectorCompatibility.PluginRequired` (`LITE2002`). Only proceed when it can prove it will not rewrite plugin-owned pages; warnings are emitted only in non-strict modes.

**Backup & Restore**:

- **Logical export/import** (documents only) always succeeds and is the recommended migration path.
- **Raw file backup** is permitted but does not change the unsupported state; restoring still requires LiteDB.Vector before vector features work.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Running `rg "Vector" LiteDB` (excluding docs/tests) yields zero matches outside extension hook definitions after the migration.
- **SC-002**: Building LiteDB without referencing LiteDB.Vector produces an unchanged public API surface (verified via API diff) compared to pre-plugin builds, aside from removed vector members.
- **SC-003**: With LiteDB.Vector installed, vector index benchmarks show ≤2% performance regression compared to the current baseline.
- **SC-004**: Plugin absence errors are consolidated into a single extensible diagnostic path, reducing user-reported “missing vector plugin” tickets by at least 80% over the next minor release.
- **SC-005**: Running LiteDB without LiteDB.Vector against databases both with and without vector metadata yields deterministic behavior—core commands succeed for non-vector data while vector-dependent data follows the documented refusal/allowance rules, verified via automated integration tests.


