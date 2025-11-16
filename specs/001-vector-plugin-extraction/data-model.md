# Data Model – Vector Plugin Isolation

## Entity: CustomIndexStrategy
- **Purpose**: Represents plugin-owned index behaviors registered with LiteDB core (ensure/drop, rebuild, metadata serializers).
- **Key Fields**:
  - `StrategyId` (string, required, unique per plugin)
  - `PluginId` (string, required)
  - `EnsureDelegate` (Func context -> bool)
  - `DropDelegate` (Func context -> bool)
  - `RebuildDelegate` (optional action)
  - `MetadataSerializerId` (string link to serializer descriptor)
- **Relationships**: References `IndexMetadataSerializer` and `PluginDiagnosticPolicy`; associated with one or more collections.
- **Validation**: StrategyId must be alphanumeric with dots; delegates must not be null; serializer reference required for any non-zero index type.

## Entity: IndexMetadataSerializer
- **Purpose**: Encodes/decodes plugin metadata stored with collection pages.
- **Key Fields**:
  - `SerializerId` (string, unique)
  - `Serialize(BsonDocument options) -> Span<byte>`
  - `Deserialize(ReadOnlySpan<byte>) -> BsonDocument`
  - `Version` (int)
- **Relationships**: Linked from `CustomIndexStrategy`; consumed by rebuild/import services.
- **Validation**: Serialized payload length must match reserved slot; version increments trigger compatibility checks.

## Entity: CustomBsonTypeDescriptor
- **Purpose**: Plugin-defined BSON type exposed through the serializer/JSON writer.
- **Key Fields**:
  - `TypeCode` (byte, must be within plugin's reserved range; e.g., `0x90-0x9F` (144-159) for LiteDB.Vector)
  - `Name` (string)
  - `Serializer` / `Deserializer` delegates
  - `JsonFormatter` delegate
- **Relationships**: Registered per `PluginId`; used by BSON reader/writer.
- **Validation**: TypeCode must be unique and within the plugin's reserved range; registrations outside reserved ranges or duplicate codes throw `InvalidOperationException`; handlers must throw informative errors when plugin absent.

## Entity: PluginPageFactory
- **Purpose**: Describes custom page instantiation for storage engine (e.g., vector index pages).
- **Key Fields**:
  - `PluginId`
  - `PageTypeCode` (byte)
  - `LogicalName` (string)
  - `FactoryDelegate(BufferSlice buffer, PageId id)`
- **Relationships**: Referenced during snapshot creation/deletion.
- **Validation**: PageTypeCode mapped uniquely; factory must support both existing and new pages.

## Entity: PluginDiagnosticPolicy
- **Purpose**: Determines how LiteDB reports plugin-related issues when functionality is missing.
- **Key Fields**:
  - `PluginId`
  - `MissingBehavior` (enum: RefuseDatabase | RefuseOperations | AllowIfSafe)
  - `MessageFormatter(ExceptionContext) -> LiteException`
- **Relationships**: Consulted by engine when encountering plugin-owned indexes/pages.
- **Validation**: MissingBehavior must align with stability guarantees; formatter must include PluginId in message text.
- **Behavior Mapping**:
  - `RefuseDatabase`: Maps to "Safe To Ignore = No" in behavior matrix; database refuses to open.
  - `RefuseOperations`: Maps to standard GA plugin behavior; database opens, vector operations fail with `LITE2002`.
  - `AllowIfSafe`: Maps to "Safe To Ignore = Yes" for prerelease artifacts; database opens with warning, operations fail only when accessing plugin-owned assets.

## Entity: QueryOperatorRegistration
- **Purpose**: Plugin-registered SQL operator/function for expression parsing and query planning.
- **Key Fields**:
  - `PluginId` (string)
  - `OperatorName` (string, e.g., `VECTOR_DIST`, `VECTOR_KNN`)
  - `ExpressionType` (BsonExpressionType enum value)
  - `Parser` (Func<BsonExpression[], BsonExpression>)
- **Relationships**: Referenced by expression parser when resolving unknown operators.
- **Validation**: OperatorName must be unique; duplicate registrations throw `InvalidOperationException`.

## Entity: PlannerCostHook
- **Purpose**: Plugin-provided cost calculation for custom index types to influence query planner decisions.
- **Key Fields**:
  - `PluginId` (string)
  - `IndexKind` (string, e.g., `vector.hnsw`)
  - `CalculateCost` (Func<QueryCostContext, double>)
- **Relationships**: Consulted by query planner when evaluating index candidates.
- **Validation**: Cost calculations must be deterministic and return non-negative values; exceptions in cost calculation cause planner to skip that index candidate.
