# Glossary – Vector Plugin Isolation

## Core Concepts

### Plugin
An optional extension assembly (e.g., `LiteDB.Vector`) that adds functionality to LiteDB core through standardized registries. Plugins register during database initialization via `ILitePlugin.Initialize()` and cannot be loaded/unloaded dynamically during runtime.

### Plugin ID
Unique identifier for a plugin (e.g., `"LiteDB.Vector"`). Used throughout diagnostics, registrations, and metadata storage to attribute plugin-owned assets. Format: reverse domain notation or simple string identifier; must be unique across all loaded plugins.

### Reserved Identifier Range
Numeric code ranges reserved for specific plugins to prevent conflicts:
- **BSON Type Codes**: `0x90-0x9F` (144-159) reserved for LiteDB.Vector
- **Page Type Codes**: `0xE0-0xEF` (224-239) reserved for LiteDB.Vector
- Core validates these during registration; overlapping registrations throw `InvalidOperationException`

---

## Index Extensibility

### Index Strategy
The **operational behavior** of a custom index type, defining how to create (Ensure), delete (Drop), and rebuild indexes. Registered via `ICustomIndexStrategyRegistry` with delegates for each operation. Example: `VectorIndexStrategy` provides the logic for ensuring/dropping HNSW vector indexes.

**Key Point**: Strategy defines *what happens* when operations are invoked; it's the executable logic.

### Index Metadata
The **persisted configuration** for a custom index stored in collection pages. Includes plugin-specific settings like dimensions, distance metrics, or algorithm parameters. Registered via `IPluginIndexMetadataRegistry` with serialization/deserialization delegates.

**Key Point**: Metadata defines *what is stored* on disk; it's the data, not the behavior.

### Index Kind
A string identifier distinguishing different index kinds *within a plugin* (e.g., `"vector.hnsw"`, `"vector.ivf"`). Used as the key in metadata registries. On disk, plugin ownership is identified by `pluginId`; supporting multiple kinds per plugin requires an explicit discriminator (payload convention or a future record-format change).

**Example**: LiteDB.Vector might register two kinds:
- `"vector.hnsw"` for HNSW indexes
- `"vector.ivf"` for IVF indexes (future)

Each kind has its own metadata serializer and may share or use separate strategies.

### Strategy vs. Metadata: Why Separate?
- **Separation of concerns**: Strategies handle runtime operations; metadata handles persistence.
- **Flexibility**: One strategy could reference multiple metadata serializers (e.g., versioned formats), or multiple strategies could share metadata structures.
- **Evolution**: Metadata schemas can version independently of operational logic.

---

## Storage Extensibility

### Page Type
A numeric code identifying the structure/purpose of a storage page (e.g., `0xE0` for `VectorIndexPage`). Core LiteDB uses codes `0x00-0xDF`; plugins reserve ranges in `0xE0-0xFF`.

### Page Factory
A delegate that constructs page instances from raw buffers. Registered via `IPageTypeRegistry` with `PageFactoryRegistration` that includes plugin ID, logical name, numeric code, and factory function. Core uses the registry when reading pages from disk.

### Page Construction Context
Immutable context passed to page factories containing buffer, page ID, and whether the page is newly allocated. Allows factories to construct pages with correct initialization based on whether they're reading existing data or creating fresh pages.

---

## BSON Extensibility

### BSON Type Code
A byte identifier for BSON value types (e.g., `0x01` = Double, `0x90` = Vector). Plugins reserve codes in designated ranges. Stored in the binary BSON stream to identify value types during deserialization.

### BSON Type Descriptor
Registration object containing plugin ID, type code, name, and serialization/deserialization/JSON formatting delegates. Registered via `ICustomBsonTypeRegistry`. Core BSON reader/writer consults registry when encountering unknown type codes.

---

## Query Extensibility

### SQL Function
A plugin-defined function callable in BsonExpression queries (e.g., `VECTOR_DIST(a, b)`). Registered via `ISqlFunctionRegistry` with parameter count constraints and implementation delegate.

### Query Operator
A plugin-defined operator/token for specialized query constructs (e.g., `VECTOR_KNN(embedding, 10)` for k-nearest-neighbor). Registered via `IQueryOperatorRegistry` with parser delegate that transforms raw expressions into executable query nodes.

### Planner Cost Hook
A plugin-provided cost calculation function that influences the query planner's index selection. Registered via `IQueryCostModelRegistry` keyed by `IndexKind`. When evaluating a query, the planner consults all registered cost hooks to determine the cheapest execution path.

### Query Cost Context
Immutable context passed to cost calculation delegates containing collection name, index metadata, query expression, and estimated document count. Allows plugins to calculate realistic cost estimates based on index characteristics and query patterns.

---

## Diagnostics & Compatibility

### Plugin Missing Behavior
Enum controlling how LiteDB responds when plugin-owned assets (metadata, pages, BSON types) are detected without the owning plugin loaded:
- **RefuseDatabase** (**default strict**): fail on first access to an affected collection (or at open when validation-on-open is enabled)
- **RefuseOperations**: open database; unaffected collections usable; any access (read/write/DDL) to affected collections throws `PLUGIN_REQUIRED`/`LITE2002`
- **AllowIfSafe**: open database; unaffected collections usable; affected collections are read-only (only `LockMode.Read`); writes/DDL are refused; warnings are emitted only in non-strict modes

### LITE2002 Diagnostic Code
Standardized error code emitted when plugin-owned functionality is accessed without the plugin installed. Message format includes:
- Plugin ID (e.g., `LiteDB.Vector`)
- Operation attempted
- Affected collection/index identifiers
- Remediation steps (install plugin, drop indexes, migrate data)

### Safe To Ignore
Boolean property in the behavior matrix indicating whether plugin-owned artifacts can be present without breaking unaffected functionality. When `true`, hosts may choose a non-strict `PluginMissingBehavior` to keep the database usable; when `false`, hosts should default to strict refusal (and may refuse open when validation-on-open is enabled).

---

## Migration & Versioning

### Prerelease Artifact
Plugin-owned metadata, pages, or BSON values created by development/prerelease builds. GA releases may refuse to process these or provide limited read-only access for migration purposes (per research decision #3).

### Metadata Version
Integer version field in plugin metadata allowing format evolution. Plugins should implement version negotiation in their deserializers; mismatched versions should fail gracefully with upgrade guidance.

### Logical Export/Import
Migration strategy that exports documents (without indexes) and reimports to fresh database. Recommended migration path for breaking changes since it bypasses low-level format compatibility issues.

### Migration Helper
Plugin-provided utility (e.g., `VectorMigrate.RebuildAll()`) that automates index drop/recreate workflows during upgrades. Lives in the plugin assembly, not core.

---

## Registry Patterns

### Registry
A singleton service that plugins use to register extension points during initialization. Core maintains registries for:
- Index strategies (`ICustomIndexStrategyRegistry`)
- Index metadata serializers (`IPluginIndexMetadataRegistry`)
- BSON types (`ICustomBsonTypeRegistry`)
- Page types (`IPageTypeRegistry`)
- SQL functions (`ISqlFunctionRegistry`)
- Query operators (`IQueryOperatorRegistry`)
- Cost models (`IQueryCostModelRegistry`)

All registries validate uniqueness and reserved code ranges during registration.

### Descriptor / Registration
Immutable record types (usually C# records) that capture the details of a plugin registration. Examples: `CustomIndexStrategyDescriptor`, `PluginIndexMetadataDescriptor`, `PageFactoryRegistration`. Descriptors are registered once during plugin initialization and remain immutable for the database lifetime.

### Plugin Context
Object passed to `ILitePlugin.Initialize()` providing access to all registries and database services. Plugins use this to register their extension points:
```csharp
public void Initialize(LiteDatabase database, ILitePluginContext context)
{
    context.RegisterBsonType(...);
    context.IndexMetadata.Register(...);
    context.RegisterPageFactory(...);
    // etc.
}
```

---

## File Organization

### Spec Documents
- **spec.md**: Main specification with user stories, requirements, behavior matrix
- **data-model.md**: Entity definitions for all plugin-related types
- **contracts/plugin-registries.md**: Interface contracts and C# signatures
- **research.md**: Design decisions with rationale and alternatives
- **tasks.md**: Implementation task breakdown with dependencies
- **plan.md**: High-level implementation plan and technical context
- **quickstart.md**: User-facing getting started guide
- **glossary.md**: This document; terminology reference

---

## Usage Examples

### Strategy + Metadata in Practice

When LiteDB.Vector initializes:

```csharp
// Register index metadata serializer (persistence)
context.RegisterIndexMetadata(new PluginIndexMetadataDescriptor(
    pluginId: "LiteDB.Vector",
    indexKind: "vector.hnsw",
    serialize: metadata => /* convert BsonDocument to byte[] */,
    deserialize: bytes => /* convert byte[] to BsonDocument */
));

// Register index strategy implementation (operations)
context.Indexes.Register(/* IIndexStrategy implementation (Ensure/Drop + maintenance hooks) */);

// Optional: register a CustomIndexStrategyDescriptor for planning/introspection
context.RegisterCustomIndexStrategy(new CustomIndexStrategyDescriptor(
    pluginId: "LiteDB.Vector",
    strategyId: "LiteDB.Vector",
    ensureIndex: ctx => /* ensure index via ctx.EnsureContext + options */,
    queryPlanner: ctx => { /* contribute planning rules */ },
    rebuildStrategy: ctx => { /* participate in rebuild */ },
    requiredBsonTypes: new[] { (byte)0x90 },
    requiredPageTypes: new[] { "VectorIndex" }
));
```

When user calls `EnsureCustomIndex` (or plugin-provided `EnsureIndex` extensions):
1. Core resolves the plugin `IIndexStrategy` by kind/type.
2. Core invokes the strategy’s ensure path under a write snapshot.
3. On success, core persists plugin index metadata to the collection page as `{pluginId}{payload}` and marks the index as plugin-owned (`IndexType != 0`).

When database opens with vector indexes:
1. Core reads metadata from collection page
2. Core extracts `pluginId` and attempts to resolve a metadata descriptor for that plugin.
3. If missing: emit `LITE2002`/`PLUGIN_REQUIRED` per host policy and refuse affected operations.
4. If present: deserialize metadata and allow the plugin to participate in planning/rebuild.

This separation means:
- Metadata format can evolve (v1 → v2 serializers) without changing strategy logic
- Multiple strategies could share metadata (HNSW variants)
- Strategies can exist without persisted metadata (ephemeral indexes)
