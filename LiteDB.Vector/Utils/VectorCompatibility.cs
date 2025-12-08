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

        public static LiteException PluginRequired() => new LiteException(LiteException.PLUGIN_REQUIRED, PluginRequiredMessage);
    }
}
