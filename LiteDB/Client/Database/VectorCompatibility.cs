using System;
using System.Linq;
using LiteDB.Plugins.Indexing;

namespace LiteDB
{
    /// <summary>
    /// Provides shared helpers for vector compatibility shims.
    /// </summary>
    internal static class VectorCompatibility
    {
        internal const string DefaultStrategyId = "LiteDB.Vector";
        internal const string DefaultStrategyKind = "vector";
        internal const string DefaultIndexKind = "vector.hnsw";

        private const string PluginRequiredMessage = "Vector index support requires the VectorSearchPlugin. Add the LiteDB.Vector package and enable the plugin when constructing LiteDatabase (e.g., new LiteDatabase(connectionString, plugins: new[] { VectorSearchPlugin.Instance })).";

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

        public static LiteException PluginRequired() => new LiteException(0, PluginRequiredMessage);
    }
}

