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
- When LiteDB(Vector) is absent yet plugin-owned metadata/page types are detected, LiteDB MUST log a single warning for the database lifetime, allow the database to open, and keep all non-plugin collections fully writable. Any operation that touches the plugin-owned index/page/BSON type MUST throw the standard `VectorCompatibility.PluginRequired` exception (with diagnostics) rather than blocking the entire database.
- How does the system behave if multiple plugins register different custom page types or BSON codes that overlap? Need deterministic precedence rules and conflict detection surfaced via plugin diagnostics.
- How are upgrade/downgrade scenarios handled when vector metadata versions change? Plugin must negotiate metadata schema or fail gracefully with actionable guidance.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Core LiteDB assemblies MUST expose vector functionality only through neutral extension points; no public `Vector*` members or enum values remain once the plugin is removed.
- **FR-002**: LiteDB.Vector MUST provide end-to-end vector index creation, drop, diagnostics, and query planning by registering services through official plugin registries rather than `InternalsVisibleTo`.
- **FR-003**: Rebuild/import flows MUST serialize vector metadata exclusively via plugin-provided `IIndexMetadataSerializer` instances and refuse to operate on metadata whose serializer is absent.
- **FR-004**: Query planning and expression parsing MUST rely on plugin-registered operators so tokens like `VECTOR_SIM` and `VECTOR_DIST` exist only when LiteDB.Vector enables them.
- **FR-005**: BSON serialization MUST allow plugin-defined type codes/handlers, enabling LiteDB.Vector to inject its vector type implementation without core awareness.
- **FR-006**: Diagnostics for missing plugin functionality MUST use plugin-agnostic wording while optionally referencing the plugin ID for guidance [NEEDS CLARIFICATION: Should diagnostics include the plugin ID in user-facing text or remain generic?].
- **FR-007**: Documentation and samples MUST describe vector capabilities as an optional plugin feature, pointing users to LiteDB.Vector installation and usage steps.
- **FR-008**: LiteDB MUST remain fully functional without LiteDB.Vector; when opening databases created with vector search enabled, the engine MUST follow one of the documented behaviors: (a) refuse to open the database up front, (b) refuse to open only documents/collections that require the missing plugin while keeping the rest stable, or (c) allow the database to open if the absence does not impact stability, and the system MUST log which path was chosen.
- **FR-009**: Core MUST replace vector-specific metadata structures with generic plugin registries: collection pages store plugin-owned metadata blobs annotated with `pluginId`, rebuild/import uses plugin-provided serializers for any custom index metadata, and the page factory registry treats plugin-reserved page types uniformly so no `Vector*` enums or helpers remain in LiteDB once the plugin is removed.

### Key Entities *(include if feature involves data)*

- **CustomIndexStrategy**: Represents plugin-defined index logic (ensure/drop delegates, metadata serializer reference, diagnostic metadata).
- **CustomBsonTypeDescriptor**: Holds plugin-registered BSON type code, serializer/deserializer delegates, and JSON formatter behavior.
- **PluginPageFactory**: Describes plugin-owned page types with logical names, numeric codes, and constructors used by storage services.

### Extensibility Interfaces *(new design details)*

| Interface/Type | Namespace | Description |
|----------------|-----------|-------------|
| `IPluginIndexMetadataRegistry` + `PluginIndexMetadataDescriptor` | `LiteDB.Plugins.Indexing` | Core exposes a registry that lets plugins register metadata serializers. Each descriptor defines `PluginId`, `string IndexKind`, `Func<byte[], PluginIndexMetadata> Deserialize`, and `Func<PluginIndexMetadata, byte[]> Serialize`. Collection pages persist metadata as `{byte pluginIdLength}{pluginIdUtf8}{byte payloadLengthLo}{byte payloadLengthHi}{payloadBytes}`, enabling the engine to store opaque blobs even when the plugin is absent. LiteDB.Vector registers a descriptor for `IndexKind = "vector"` that understands the existing slot/dimension/metric payload. |
| `IPageTypeRegistry` + extended `PageFactoryRegistration` | `LiteDB.Plugins.Storage` | Builds on the current page factory registry by adding explicit page-type declarations: plugins call `context.RegisterPageFactory(new PageFactoryRegistration(pluginId: "LiteDB.Vector", pageType: "VectorIndex", numericCode: 0x80, compatibilityRange: ">=8.0", factory: ...))`. Persisted pages record the numeric code plus the pluginId so that, if the plugin is missing, `PageFactoryRegistry` can warn once and throw `VectorCompatibility.PluginRequired()` only when that page type is accessed. |
| `IBsonTypeRegistry` + `PluginBsonTypeRegistration` | `LiteDB.Plugins.Bson` | Plugins reserve BSON type codes using `context.RegisterBsonType(new BsonTypeRegistration(pluginId, typeCode, name, serializer, deserializer, legacyAliases))`. LiteDB.Vector supplies the `BsonType.Vector` handler (legacy alias `0x64`) so BSON serialization/deserialization flows through the plugin. Core keeps `LiteDB.Core` registrations for built-in types but no longer contains vector-specific serializers. |

**LiteDB.Vector Migration Path**

1. During `VectorSearchPlugin.Initialize`, register the BSON handler, metadata descriptor (`IndexKind = "vector"`), and page factory (`pageType = "VectorIndex"`) before hooking up query/index services.
2. When the plugin is absent, collection pages still contain `{pluginId="LiteDB.Vector", payload=legacy blob}` records. The registry reports “missing serializer” so core emits the standardized `plugin_required` diagnostic while leaving other collections writable.
3. Rebuild/import/FileReader consult `IPluginIndexMetadataRegistry` to serialize/deserialize plugin-owned metadata. If no serializer exists, they emit the same diagnostic and skip the affected index.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Running `rg "Vector" LiteDB` (excluding docs/tests) yields zero matches outside extension hook definitions after the migration.
- **SC-002**: Building LiteDB without referencing LiteDB.Vector produces an unchanged public API surface (verified via API diff) compared to pre-plugin builds, aside from removed vector members.
- **SC-003**: With LiteDB.Vector installed, vector index benchmarks show ≤2% performance regression compared to the current baseline.
- **SC-004**: Plugin absence errors are consolidated into a single extensible diagnostic path, reducing user-reported “missing vector plugin” tickets by at least 80% over the next minor release.
- **SC-005**: Running LiteDB without LiteDB.Vector against databases both with and without vector metadata yields deterministic behavior—core commands succeed for non-vector data while vector-dependent data follows the documented refusal/allowance rules, verified via automated integration tests.
