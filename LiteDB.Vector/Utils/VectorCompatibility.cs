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

        private const string PluginRequiredMessage = "Vector index support requires the LiteDB.Vector plugin. Install the LiteDB.Vector package and register VectorSearchPlugin.Instance (for example, new LiteDatabase(connectionString, plugins: new[] { VectorSearchPlugin.Instance })) before performing vector operations.";

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

        public static string BuildMissingPluginMessage(string operation)
        {
            var targetOperation = string.IsNullOrWhiteSpace(operation) ? "the requested operation" : operation;

            return $"Vector index support requires the LiteDB.Vector plugin. Install the LiteDB.Vector package and register VectorSearchPlugin.Instance (for example, new LiteDatabase(connectionString, plugins: new[] {{ VectorSearchPlugin.Instance }})) before performing '{targetOperation}'.";
        }

        public static LiteException PluginRequired() => new LiteException(LiteException.PLUGIN_REQUIRED, PluginRequiredMessage);
    }
}
