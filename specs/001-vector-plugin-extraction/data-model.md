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
  - `TypeCode` (byte, >99 to avoid conflicts)
  - `Name` (string)
  - `Serializer` / `Deserializer` delegates
  - `JsonFormatter` delegate
- **Relationships**: Registered per `PluginId`; used by BSON reader/writer.
- **Validation**: TypeCode unique; handlers must throw informative errors when plugin absent.

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
  - `MissingBehavior` (enum: RefuseDatabase | RefuseOperations | Allow)
  - `MessageFormatter(ExceptionContext) -> LiteException`
- **Relationships**: Consulted by engine when encountering plugin-owned indexes/pages.
- **Validation**: MissingBehavior must align with stability guarantees; formatter must include PluginId in message text.
