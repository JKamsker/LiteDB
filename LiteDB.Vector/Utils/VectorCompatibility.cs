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

        private const string PluginRequiredMessage = "Vector index support requires the LiteDB.Vector plugin. Install the LiteDB.Vector package and register VectorSearchPlugin.Instance (for example, new LiteDatabase(connectionString, plugins: new[] { VectorSearchPlugin.Instance })).";

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
                throw new LiteException(0, PluginRequiredMessage);
            }

            return descriptor;
        }

        public static LiteException PluginRequired(BsonDocument diagnostics = null)
        {
            var exception = new LiteException(LiteException.PLUGIN_REQUIRED, PluginRequiredMessage);

            if (diagnostics != null)
            {
                try
                {
                    exception.Data["VectorDiagnostics"] = diagnostics;
                }
                catch (ArgumentException)
                {
                    exception.Data["VectorDiagnostics"] = diagnostics.ToString();
                }
            }

            return exception;
        }

        public static BsonDocument CreateMissingPluginDiagnostics(string operation, string collection, string strategyKind, ILitePluginContext pluginContext, BsonDocument options = null)
        {
            var diagnostics = new BsonDocument
            {
                ["event"] = "plugin.index_required",
                ["operation"] = operation ?? string.Empty,
                ["collection"] = collection ?? string.Empty,
                ["index"] = string.Empty,
                ["expression"] = string.Empty,
                ["strategyKind"] = strategyKind ?? string.Empty,
                ["pluginContextAvailable"] = pluginContext != null,
                ["strategyRegistryAvailable"] = pluginContext?.CustomIndexes != null,
                ["pluginId"] = VectorPlugin.PluginId,
                ["registeredStrategies"] = GetRegisteredCustomStrategies(pluginContext)
            };

            if (options != null && options.Count > 0)
            {
                var copy = new BsonDocument();
                options.CopyTo(copy);
                diagnostics["options"] = copy;
            }

            return diagnostics;
        }

        private static BsonArray GetRegisteredCustomStrategies(ILitePluginContext pluginContext)
        {
            var array = new BsonArray();
            var registered = pluginContext?.CustomIndexes?.Registered;

            if (registered != null)
            {
                foreach (var descriptor in registered)
                {
                    if (descriptor?.StrategyId != null)
                    {
                        array.Add(descriptor.StrategyId);
                    }
                }
            }

            return array;
        }
    }
}
