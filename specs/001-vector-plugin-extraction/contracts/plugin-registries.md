# Contract: Plugin Registries for Vector Isolation

```csharp
namespace LiteDB.Plugins
{
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
}
```

```csharp
namespace LiteDB.Plugins.Bson
{
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

```csharp
namespace LiteDB.Engine.Plugins
{
    public interface IPluginDiagnosticPolicy
    {
        PluginMissingBehavior MissingBehavior { get; }
        LiteException CreateMissingPluginException(string pluginId, string operation, BsonDocument diagnostics);
    }
}
```

- `PluginMissingBehavior` enum values: `RefuseDatabase`, `RefuseOperations`, `AllowIfSafe`.
- LiteDB core will invoke `IPluginDiagnosticPolicy` whenever it resolves a plugin-owned asset without the plugin being loaded; policies must include the plugin ID in the generated message.
