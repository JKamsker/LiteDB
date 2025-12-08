using System;
using System.Linq;
using LiteDB;
using LiteDB.Plugins;
using LiteDB.Plugins.Indexing;
using LiteDB.Vector;

namespace LiteDB.Vector.Utils
{
    /// <summary>
    /// Provides shared helpers for vector compatibility shims inside the plugin.
    /// </summary>
    internal static class VectorCompatibility
    {
        internal const string DefaultStrategyId = VectorPlugin.PluginId;
        internal const string DefaultStrategyKind = VectorPlugin.StrategyKind;
        internal const string DefaultIndexKind = VectorPlugin.IndexKind;

        private const string PluginRequiredMessage = "Vector index support requires the LiteDB.Vector plugin. Add the LiteDB.Vector package and enable the plugin when constructing LiteDatabase (e.g., new LiteDatabase(connectionString, plugins: new[] { VectorSearchPlugin.Instance })).";

        public static CustomIndexStrategyDescriptor TryGetStrategy(ICustomIndexStrategyRegistry registry)
        {
            if (registry == null)
            {
                return null;
            }

            if (registry.TryGet(DefaultStrategyId, out var descriptor))
            {
                return descriptor;
            }

            var registered = registry.Registered;
            if (registered != null && registered.Count == 1)
            {
                return registered.First();
            }

            return null;
        }

        public static CustomIndexStrategyDescriptor RequireStrategy(ICustomIndexStrategyRegistry registry)
        {
            var descriptor = TryGetStrategy(registry);

            if (descriptor == null)
            {
                throw PluginRequired(operation: "EnsureCustomIndex", strategyKind: DefaultStrategyKind, pluginContext: null);
            }

            return descriptor;
        }

        public static LiteException PluginRequired(
            string operation = null,
            string collection = null,
            string strategyKind = null,
            string indexName = null,
            string expression = null,
            BsonDocument options = null,
            ILitePluginContext pluginContext = null)
        {
            var diagnostics = new BsonDocument
            {
                ["event"] = "plugin.index_required",
                ["operation"] = operation ?? string.Empty,
                ["collection"] = collection ?? string.Empty,
                ["index"] = indexName ?? string.Empty,
                ["expression"] = expression ?? string.Empty,
                ["strategyKind"] = strategyKind ?? string.Empty,
                ["pluginContextAvailable"] = pluginContext != null,
                ["strategyRegistryAvailable"] = pluginContext?.CustomIndexes != null,
                ["pluginId"] = VectorPlugin.PluginId,
                ["registeredStrategies"] = GetRegisteredCustomStrategies(pluginContext?.CustomIndexes)
            };

            if (options != null && options.Count > 0)
            {
                var optionCopy = new BsonDocument();
                options.CopyTo(optionCopy);
                diagnostics["options"] = optionCopy;
            }

            var policy = pluginContext?.DiagnosticPolicy;

            if (policy == null || policy is DefaultPluginDiagnosticPolicy)
            {
                policy = VectorPluginDiagnosticPolicy.Instance;
            }

            return policy.CreateMissingPluginException(VectorPlugin.PluginId, operation ?? "VectorOperation", diagnostics);
        }

        private static BsonArray GetRegisteredCustomStrategies(ICustomIndexStrategyRegistry registry)
        {
            var registered = registry?.Registered;

            if (registered == null || registered.Count == 0)
            {
                return new BsonArray();
            }

            var array = new BsonArray();

            foreach (var descriptor in registered)
            {
                if (descriptor == null)
                {
                    continue;
                }

                array.Add(new BsonDocument
                {
                    ["pluginId"] = descriptor.PluginId ?? string.Empty,
                    ["strategyId"] = descriptor.StrategyId ?? string.Empty
                });
            }

            return array;
        }
    }
}
