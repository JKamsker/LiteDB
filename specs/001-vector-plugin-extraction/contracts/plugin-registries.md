# Contract: Plugin Registries for Vector Isolation

## Index Strategy Registry

```csharp
namespace LiteDB.Plugins.Indexing
{
    /// <summary>
    /// Registry for plugin-defined index strategies (Ensure/Drop/Rebuild operations).
    /// Separate from metadata serialization to allow strategies to reference multiple serializers.
    /// </summary>
    public interface ICustomIndexStrategyRegistry
    {
        void Register(CustomIndexStrategyDescriptor descriptor);
        bool TryGet(string strategyId, out CustomIndexStrategyDescriptor descriptor);
    }

    public sealed record CustomIndexStrategyDescriptor(
        string PluginId,
        string StrategyId,
        Func<EnsureIndexContext, bool> Ensure,
        Func<DropIndexContext, bool> Drop,
        Action<RebuildIndexContext>? Rebuild,
        string MetadataSerializerId);

    /// <summary>
    /// Context provided to plugin's Ensure index delegate.
    /// </summary>
    public sealed class EnsureIndexContext
    {
        public required string CollectionName { get; init; }
        public required string IndexName { get; init; }
        public required BsonExpression Expression { get; init; }
        public required BsonDocument Options { get; init; }
        public required ILiteEngine Engine { get; init; }
    }

    /// <summary>
    /// Context provided to plugin's Drop index delegate.
    /// </summary>
    public sealed class DropIndexContext
    {
        public required string CollectionName { get; init; }
        public required string IndexName { get; init; }
        public required ILiteEngine Engine { get; init; }
    }

    /// <summary>
    /// Context provided to plugin's Rebuild index delegate.
    /// </summary>
    public sealed class RebuildIndexContext
    {
        public required string CollectionName { get; init; }
        public required string IndexName { get; init; }
        public required BsonDocument Metadata { get; init; }
        public required ILiteEngine Engine { get; init; }
    }
}
```

## Index Metadata Registry

```csharp
namespace LiteDB.Plugins.Indexing
{
    /// <summary>
    /// Registry for plugin-defined index metadata serializers.
    /// Collection pages persist metadata as opaque blobs; plugins provide serialization logic.
    /// </summary>
    public interface IPluginIndexMetadataRegistry
    {
        void Register(PluginIndexMetadataDescriptor descriptor);
        bool TryGet(string indexKind, out PluginIndexMetadataDescriptor descriptor);
    }

    public sealed record PluginIndexMetadataDescriptor(
        string PluginId,
        string IndexKind,
        Func<BsonDocument, byte[]> Serialize,
        Func<byte[], BsonDocument> Deserialize);

    /// <summary>
    /// Generic metadata payload stored in collection pages for plugin-owned indexes.
    /// Core persists this as: {pluginIdLength:byte}{pluginId:utf8}{payloadLength:ushort}{payload:bytes}
    /// </summary>
    public sealed class PluginIndexMetadata
    {
        public required string PluginId { get; init; }
        public required string IndexKind { get; init; }
        public required byte[] Payload { get; init; }
    }
}
```

## BSON Type Registry

```csharp
namespace LiteDB.Plugins.Bson
{
    /// <summary>
    /// Registry for plugin-defined BSON types.
    /// Plugins reserve type codes in designated ranges (e.g., 0x90-0x9F for LiteDB.Vector).
    /// </summary>
    public interface ICustomBsonTypeRegistry
    {
        void Register(CustomBsonTypeDescriptor descriptor);
        bool TryGet(byte typeCode, out CustomBsonTypeDescriptor descriptor);
    }

    public sealed record CustomBsonTypeDescriptor(
        string PluginId,
        byte TypeCode,
        string Name,
        Func<BsonValue, BufferWriter, bool> Serializer,
        Func<BufferReader, BsonValue> Deserializer,
        Func<BsonValue, string> JsonFormatter);
}
```

## Page Type Registry

```csharp
namespace LiteDB.Plugins.Storage
{
    /// <summary>
    /// Registry for plugin-defined page types.
    /// Plugins reserve numeric page codes (e.g., 0xE0-0xEF for LiteDB.Vector).
    /// Persisted pages store the numeric code plus pluginId for deterministic error handling.
    /// </summary>
    public interface IPageTypeRegistry
    {
        void Register(PageFactoryRegistration registration);
        bool TryGet(byte pageTypeCode, out PageFactoryRegistration registration);
        bool TryGetByName(string pluginId, string pageTypeName, out PageFactoryRegistration registration);
    }

    public sealed record PageFactoryRegistration(
        string PluginId,
        string PageType,
        byte NumericCode,
        string CompatibilityRange,
        Func<PageConstructionContext, BasePage> Factory);

    /// <summary>
    /// Context provided to page factory delegates.
    /// </summary>
    public sealed class PageConstructionContext
    {
        public required BufferSlice Buffer { get; init; }
        public required uint PageId { get; init; }
        public required bool IsNewPage { get; init; }
    }
}
```

## Query Extensibility Registries

```csharp
namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Registry for plugin-defined SQL functions.
    /// Example: LiteDB.Vector registers VECTOR_DIST, VECTOR_SIM functions.
    /// </summary>
    public interface ISqlFunctionRegistry
    {
        void Register(SqlFunctionRegistration registration);
        bool TryGet(string functionName, out SqlFunctionRegistration registration);
    }

    public sealed record SqlFunctionRegistration(
        string PluginId,
        string FunctionName,
        Func<BsonValue[], BsonValue> Implementation,
        int MinParameterCount,
        int MaxParameterCount);

    /// <summary>
    /// Registry for plugin-defined query operators.
    /// Example: LiteDB.Vector registers VECTOR_KNN operator for k-nearest-neighbor queries.
    /// </summary>
    public interface IQueryOperatorRegistry
    {
        void Register(QueryOperatorRegistration registration);
        bool TryGet(string operatorName, out QueryOperatorRegistration registration);
    }

    public sealed record QueryOperatorRegistration(
        string PluginId,
        string OperatorName,
        BsonExpressionType ExpressionType,
        Func<BsonExpression[], BsonExpression> Parser);

    /// <summary>
    /// Registry for plugin-provided query planner cost models.
    /// Plugins can influence index selection by advertising costs for their custom index types.
    /// </summary>
    public interface IQueryCostModelRegistry
    {
        void Register(QueryCostModelRegistration registration);
        IEnumerable<QueryCostModelRegistration> GetAll();
    }

    public sealed record QueryCostModelRegistration(
        string PluginId,
        string IndexKind,
        Func<QueryCostContext, double> CalculateCost);

    /// <summary>
    /// Context provided to cost calculation delegates.
    /// </summary>
    public sealed class QueryCostContext
    {
        public required string CollectionName { get; init; }
        public required string IndexName { get; init; }
        public required BsonExpression Query { get; init; }
        public required BsonDocument IndexMetadata { get; init; }
        public required long EstimatedDocumentCount { get; init; }
    }
}
```

## Plugin Diagnostic Policy

```csharp
namespace LiteDB.Engine.Plugins
{
    /// <summary>
    /// Policy determining how LiteDB handles missing plugin dependencies.
    /// Core invokes this whenever it encounters plugin-owned assets without the plugin loaded.
    /// </summary>
    public interface IPluginDiagnosticPolicy
    {
        PluginMissingBehavior MissingBehavior { get; }
        LiteException CreateMissingPluginException(string pluginId, string operation, BsonDocument diagnostics);
    }

    /// <summary>
    /// Defines how the database behaves when plugin-owned assets are detected without the plugin.
    /// </summary>
    public enum PluginMissingBehavior
    {
        /// <summary>
        /// Refuse to open the database entirely.
        /// Use when plugin-owned assets are unsafe to ignore (e.g., corrupt critical metadata).
        /// </summary>
        RefuseDatabase,

        /// <summary>
        /// Open the database but throw exceptions when plugin-owned assets are accessed.
        /// Non-plugin collections remain fully functional.
        /// This is the default behavior for most plugins.
        /// </summary>
        RefuseOperations,

        /// <summary>
        /// Allow database to open and defer to runtime safety checks.
        /// Only use when plugin absence can be safely detected per-operation.
        /// Maps to "Safe To Ignore = Yes" in the behavior matrix.
        /// </summary>
        AllowIfSafe
    }
}
```

## Notes

- **Conflict Detection**: All registries must validate that plugin IDs and reserved codes (BSON type codes, page type codes) do not conflict. Duplicate registrations throw `InvalidOperationException` with diagnostic details.
- **Thread Safety**: All registry implementations must be thread-safe for concurrent reads after initial registration.
- **Registration Order**: Plugins must register all extension points during `ILitePlugin.Initialize()` before the database opens. Late registration is not supported.
- **Diagnostic Messages**: All plugin-missing exceptions MUST include the `PluginId` in user-facing error text per research decision #1.
