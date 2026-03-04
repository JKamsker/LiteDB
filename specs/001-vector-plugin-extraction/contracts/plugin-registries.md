# Contract: Plugin Registries for Vector Isolation

This document mirrors the current in-repo plugin extensibility APIs. The source of truth is the corresponding `LiteDB/Plugins/**` files referenced in each section.

## Index Strategy Registry

Source: `LiteDB/Plugins/Indexing/CustomIndexStrategyDescriptor.cs`

```csharp
namespace LiteDB.Plugins.Indexing
{
    public interface ICustomIndexStrategyRegistry
    {
        void Register(CustomIndexStrategyDescriptor descriptor);
        bool TryGet(string strategyId, out CustomIndexStrategyDescriptor descriptor);
        CustomIndexStrategyDescriptor Get(string strategyId);
        IReadOnlyCollection<CustomIndexStrategyDescriptor> Registered { get; }
    }

    public delegate bool CustomIndexEnsureDelegate(CustomIndexEnsureContext context);
    public delegate void CustomIndexQueryPlannerDelegate(CustomIndexQueryPlannerContext context);
    public delegate void CustomIndexRebuildDelegate(CustomIndexRebuildContext context);

    public sealed class CustomIndexStrategyDescriptor
    {
        public CustomIndexStrategyDescriptor(
            string pluginId,
            string strategyId,
            CustomIndexEnsureDelegate ensureIndex,
            CustomIndexQueryPlannerDelegate queryPlanner,
            CustomIndexRebuildDelegate rebuildStrategy = null,
            IReadOnlyCollection<byte> requiredBsonTypes = null,
            IReadOnlyCollection<string> requiredPageTypes = null);
    }

    public sealed class CustomIndexEnsureContext
    {
        public CustomIndexEnsureContext(EnsureIndexContext ensureContext, BsonDocument options);
        public EnsureIndexContext EnsureContext { get; }
        public BsonDocument Options { get; }
    }

    public sealed class CustomIndexQueryPlannerContext
    {
        public CustomIndexQueryPlannerContext(QueryPlanningContext planningContext);
        public QueryPlanningContext PlanningContext { get; }
    }

    public sealed class CustomIndexRebuildContext
    {
        public CustomIndexRebuildContext(LiteEngine engine, ILitePluginContext pluginContext);
        public LiteEngine Engine { get; }
        public ILitePluginContext PluginContext { get; }
    }
}
```

## Index Metadata Registry

Source: `LiteDB/Plugins/Indexing/IPluginIndexMetadataRegistry.cs`

```csharp
namespace LiteDB.Plugins.Indexing
{
    public interface IPluginIndexMetadataRegistry
    {
        void Register(PluginIndexMetadataDescriptor descriptor);
        bool TryGet(string indexKind, out PluginIndexMetadataDescriptor descriptor);
        PluginIndexMetadataDescriptor Get(string indexKind);
        IReadOnlyCollection<PluginIndexMetadataDescriptor> Registered { get; }
    }

    public sealed class PluginIndexMetadataDescriptor
    {
        public PluginIndexMetadataDescriptor(
            string pluginId,
            string indexKind,
            Func<BsonDocument, byte[]> serialize,
            Func<byte[], BsonDocument> deserialize);

        public string PluginId { get; }
        public string IndexKind { get; }
        public Func<BsonDocument, byte[]> Serialize { get; }
        public Func<byte[], BsonDocument> Deserialize { get; }
    }

    public sealed class PluginIndexMetadata
    {
        public PluginIndexMetadata(string pluginId, string indexKind, byte[] payload);
        public string PluginId { get; }
        public string IndexKind { get; }
        public byte[] Payload { get; }
    }
}
```

Notes:

- Collection pages persist `{pluginId, payload}`. `IndexKind` is not explicitly persisted today, so metadata descriptor resolution assumes one descriptor per pluginId unless an explicit discriminator is added.

## BSON Type Registry

Source: `LiteDB/Plugins/Bson/CustomBsonTypeDescriptor.cs`

```csharp
namespace LiteDB.Plugins.Bson
{
    public interface ICustomBsonTypeRegistry
    {
        void Register(CustomBsonTypeDescriptor descriptor);
        bool TryGetByTypeCode(byte typeCode, out CustomBsonTypeDescriptor descriptor);
        bool TryGetByName(string name, out CustomBsonTypeDescriptor descriptor);
        IReadOnlyCollection<CustomBsonTypeDescriptor> Registered { get; }
    }

    public sealed class CustomBsonTypeDescriptor
    {
        public CustomBsonTypeDescriptor(
            string pluginId,
            byte typeCode,
            string name,
            Func<BsonValue, int> calculateSize,
            Action<object, BsonValue> serializer,
            Func<object, BsonValue> deserializer,
            Func<BsonValue, string> jsonFormatter,
            IReadOnlyCollection<byte> legacyAliases = null);
    }
}
```

## Page Type Registry

Source: `LiteDB/Plugins/Storage/IPageTypeRegistry.cs`

```csharp
namespace LiteDB.Plugins.Storage
{
    public interface IPageTypeRegistry
    {
        void Register(PageFactoryRegistration registration);
        bool TryGet(byte pageTypeCode, out PageFactoryRegistration registration);
        bool TryGet(string pageTypeName, out PageFactoryRegistration registration);
        bool TryGetByName(string pluginId, string pageTypeName, out PageFactoryRegistration registration);
        IReadOnlyCollection<PageFactoryRegistration> Registered { get; }
    }

    public sealed class PageFactoryRegistration
    {
        public PageFactoryRegistration(
            string pluginId,
            string pageType,
            byte numericCode,
            string compatibilityRange,
            Func<PageConstructionContext, object> factory);
    }

    public sealed class PageConstructionContext
    {
        public PageConstructionContext(object buffer, uint pageId, bool isNewPage);
        public object Buffer { get; }
        public uint PageId { get; }
        public bool IsNewPage { get; }
    }
}
```

Notes:

- Persisted pages record the numeric page code. Decoding an unregistered plugin page type throws when that page type is accessed.

## Query Extensibility Registries

Sources:

- `LiteDB/Plugins/Query/ISqlFunctionRegistry.cs`
- `LiteDB/Plugins/Query/IQueryOperatorRegistry.cs`
- `LiteDB/Plugins/Query/IQueryCostModelRegistry.cs`

```csharp
namespace LiteDB.Plugins.Query
{
    public interface ISqlFunctionRegistry
    {
        void Register(SqlFunctionRegistration registration);
        bool TryGet(string functionName, out SqlFunctionRegistration registration);
        IReadOnlyCollection<SqlFunctionRegistration> Registered { get; }
    }

    public sealed class SqlFunctionRegistration
    {
        public SqlFunctionRegistration(
            string pluginId,
            string functionName,
            Func<BsonValue[], BsonValue> implementation,
            int minParameterCount,
            int maxParameterCount);
    }

    public interface IQueryOperatorRegistry
    {
        void Register(QueryOperatorRegistration registration);
        bool TryGet(string operatorName, out QueryOperatorRegistration registration);
        IReadOnlyCollection<QueryOperatorRegistration> Registered { get; }
    }

    public sealed class QueryOperatorRegistration
    {
        public QueryOperatorRegistration(
            string pluginId,
            string operatorName,
            BsonExpressionType expressionType,
            Func<BsonExpression[], BsonExpression> parser,
            BinaryOperatorPrecedence precedence = BinaryOperatorPrecedence.Comparison);
    }

    public interface IQueryCostModelRegistry
    {
        void Register(QueryCostModelRegistration registration);
        IReadOnlyCollection<QueryCostModelRegistration> Registered { get; }
    }

    public sealed class QueryCostModelRegistration
    {
        public QueryCostModelRegistration(
            string pluginId,
            string indexKind,
            Func<QueryCostContext, double> calculateCost);
    }

    public sealed class QueryCostContext
    {
        public QueryCostContext(
            string collectionName,
            string indexName,
            BsonExpression query,
            BsonDocument indexMetadata,
            long estimatedDocumentCount);
    }
}
```

## Plugin Diagnostic Policy

Source: `LiteDB/Plugins/PluginDiagnosticPolicy.cs`

```csharp
namespace LiteDB.Plugins
{
    public interface IPluginDiagnosticPolicy
    {
        PluginMissingBehavior MissingBehavior { get; }
        LiteException CreateMissingPluginException(string pluginId, string operation, BsonDocument diagnostics);
    }

    public enum PluginMissingBehavior
    {
        RefuseDatabase,
        RefuseOperations,
        AllowIfSafe
    }
}
```

## Notes

- **Conflict Detection**: All registries must validate that plugin IDs and reserved codes (BSON type codes, page type codes) do not conflict. Duplicate registrations throw `InvalidOperationException` with diagnostic details.
- **Thread Safety**: All registry implementations must be thread-safe for concurrent reads after initial registration.
- **Registration Order**: Plugins must register all extension points during `ILitePlugin.Initialize()` before the database opens. Late registration is not supported.
- **Diagnostic Messages**: All plugin-missing exceptions MUST include the `PluginId` in user-facing error text per research decision #1.
