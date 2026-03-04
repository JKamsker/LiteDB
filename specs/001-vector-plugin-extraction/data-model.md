# Data Model – Vector Plugin Isolation

This file describes the key plugin extensibility entities as they exist in the current codebase. Canonical signatures live in `specs/001-vector-plugin-extraction/contracts/plugin-registries.md`.

## Entity: CustomIndexStrategyDescriptor

- **Purpose**: Declares plugin-owned index behaviour (ensure + query planning + optional rebuild orchestration) plus required dependencies.
- **Key Fields**:
  - `PluginId` (string, required)
  - `StrategyId` (string, required, unique per plugin)
  - `EnsureIndex` (`CustomIndexEnsureDelegate`, required)
  - `QueryPlanner` (`CustomIndexQueryPlannerDelegate`, required)
  - `RebuildStrategy` (`CustomIndexRebuildDelegate`, optional)
  - `RequiredBsonTypes` (array<byte>, optional)
  - `RequiredPageTypes` (array<string>, optional)
- **Relationships**: Registered via `ICustomIndexStrategyRegistry`; invoked by engine/index services when ensuring indexes, planning queries, and coordinating rebuild flows.
- **Validation**: Strategy IDs must be unique; required dependencies must be registered before activation; delegates must not perform I/O during plugin initialization (registration-only).

## Entity: PluginIndexMetadataDescriptor

- **Purpose**: Serializes/deserializes plugin-owned index metadata payloads persisted in collection pages.
- **Key Fields**:
  - `PluginId` (string, required)
  - `IndexKind` (string, required; registry key within the plugin)
  - `Serialize` (`Func<BsonDocument, byte[]>`)
  - `Deserialize` (`Func<byte[], BsonDocument>`)
- **Relationships**: Registered via `IPluginIndexMetadataRegistry`; used by rebuild/import and diagnostics to interpret metadata payloads.
- **Notes**: On disk, collection pages persist `{pluginId, payload}`. `IndexKind` is not explicitly persisted today, so descriptor resolution assumes one descriptor per `pluginId` unless a discriminator is introduced.

## Entity: CustomBsonTypeDescriptor

- **Purpose**: Declares a plugin-owned BSON type code and handlers used by BSON reader/writer.
- **Key Fields**:
  - `PluginId` (string, required)
  - `TypeCode` (byte, plugin codes must be `>= 128`; e.g., `0x90-0x9F` reserved for LiteDB.Vector)
  - `Name` (string)
  - `CalculateSize` (`Func<BsonValue, int>`)
  - `Serializer` (`Action<object, BsonValue>`)
  - `Deserializer` (`Func<object, BsonValue>`)
  - `JsonFormatter` (`Func<BsonValue, string>`)
  - `LegacyAliases` (array<byte>, optional)
- **Relationships**: Registered via `ICustomBsonTypeRegistry`; used by BSON serialization/deserialization.
- **Validation**: Type codes must be unique and within reserved ranges; duplicate registrations throw; handlers must be deterministic.

## Entity: PageFactoryRegistration

- **Purpose**: Declares plugin-owned page types backed by deterministic numeric page codes.
- **Key Fields**:
  - `PluginId` (string, required)
  - `PageType` (string, logical name)
  - `NumericCode` (byte; e.g., `0xE0-0xEF` reserved for LiteDB.Vector)
  - `CompatibilityRange` (string)
  - `Factory` (`Func<PageConstructionContext, object>`)
- **Relationships**: Registered via `IPageTypeRegistry`; used when decoding plugin-owned pages by numeric code.
- **Notes**: Persisted pages record the numeric code. Decoding an unregistered plugin page type throws when the page is accessed.

## Entity: PluginDiagnosticPolicy

- **Purpose**: Determines how LiteDB reports and enforces missing-plugin behaviour when plugin-owned indexes are detected without the owning plugin loaded.
- **Key Fields**:
  - `MissingBehavior` (enum: `RefuseDatabase` | `RefuseOperations` | `AllowIfSafe`)
  - `CreateMissingPluginException(pluginId, operation, diagnostics) -> LiteException`
- **Behavior Mapping**:
  - `RefuseDatabase` (**default strict**): fail on first access to an affected collection (or at open when validation-on-open is enabled).
  - `RefuseOperations`: open database; unaffected collections usable; any access (read/write/DDL) to affected collections fails with `PLUGIN_REQUIRED`/`LITE2002`.
  - `AllowIfSafe`: open database; unaffected collections usable; affected collections are read-only (only `LockMode.Read`); writes/DDL fail; warnings are emitted only in non-strict modes.

## Entity: SqlFunctionRegistration

- **Purpose**: Plugin-registered SQL function callable in BsonExpression queries (e.g., `VECTOR_DIST(a, b)`).
- **Key Fields**:
  - `PluginId` (string)
  - `FunctionName` (string)
  - `Implementation` (`Func<BsonValue[], BsonValue>`)
  - `MinParameterCount` / `MaxParameterCount` (int; `-1` for variadic)

## Entity: QueryOperatorRegistration

- **Purpose**: Plugin-registered operator/token for expression parsing and query planning (e.g., `VECTOR_KNN(...)`).
- **Key Fields**:
  - `PluginId` (string)
  - `OperatorName` (string)
  - `ExpressionType` (BsonExpressionType enum value)
  - `Parser` (`Func<BsonExpression[], BsonExpression>`)
  - `Precedence` (BinaryOperatorPrecedence)

## Entity: QueryCostModelRegistration

- **Purpose**: Plugin-provided cost model to influence query planner decisions for plugin-owned indexes.
- **Key Fields**:
  - `PluginId` (string)
  - `IndexKind` (string, e.g., `vector.hnsw`)
  - `CalculateCost` (`Func<QueryCostContext, double>`)
