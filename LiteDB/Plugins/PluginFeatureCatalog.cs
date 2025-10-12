using System;
using System.Collections.Generic;
using System.Reflection;

namespace LiteDB.Plugins
{
    internal static class PluginFeatureCatalog
    {
        private static readonly Dictionary<string, string> _featureTypeNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["vector"] = "LiteDB.Vector.VectorSearchPlugin, LiteDB.Vector"
        };

        public static bool TryCreate(string feature, out ILitePlugin plugin)
        {
            plugin = null;

            if (string.IsNullOrWhiteSpace(feature))
            {
                return false;
            }

            if (!_featureTypeNames.TryGetValue(feature, out var typeName))
            {
                return false;
            }

            try
            {
                var type = Type.GetType(typeName, throwOnError: false);

                if (type == null || !typeof(ILitePlugin).IsAssignableFrom(type))
                {
                    return false;
                }

                object instance = null;
                var instanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);

                if (instanceProperty != null && instanceProperty.CanRead)
                {
                    instance = instanceProperty.GetValue(null);
                }

                if (instance == null)
                {
                    if (type.GetConstructor(Type.EmptyTypes) != null)
                    {
                        instance = Activator.CreateInstance(type);
                    }
                }

                plugin = instance as ILitePlugin;
                return plugin != null;
            }
            catch
            {
                plugin = null;
                return false;
            }
        }
    }
}
