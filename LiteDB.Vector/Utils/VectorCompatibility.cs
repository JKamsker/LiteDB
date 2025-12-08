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
        internal const string MissingPluginMessagePrefix = "Vector index support requires the LiteDB.Vector plugin.";

        private const string PluginRegistrationGuidance = "Install the LiteDB.Vector package and register VectorSearchPlugin.Instance (for example, new LiteDatabase(connectionString, plugins: new[] { VectorSearchPlugin.Instance })) before performing '{0}'.";

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
                throw CreateMissingPluginException("EnsureCustomIndex", BuildIndexDiagnostics(registry, operation: "EnsureCustomIndex"));
            }

            return descriptor;
        }

        public static LiteException PluginRequired(
            string operation = null,
            ILitePluginContext pluginContext = null,
            ICustomIndexStrategyRegistry strategyRegistry = null,
            string collection = null,
            string index = null,
            string strategyKind = null,
            string @event = "plugin.index_required")
        {
            var diagnostics = BuildIndexDiagnostics(strategyRegistry ?? pluginContext?.CustomIndexes, pluginContext, collection, index, strategyKind, operation, @event);

            return CreateMissingPluginException(operation, diagnostics);
        }

        internal static LiteException CreateMissingPluginException(string operation, BsonDocument diagnostics)
        {
            var message = BuildMissingPluginMessage(operation);
            var exception = new LiteException(LiteException.PLUGIN_REQUIRED, message);

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

        internal static string BuildMissingPluginMessage(string operation)
        {
            var subject = operation ?? "the requested operation";
            return $"{MissingPluginMessagePrefix} {string.Format(PluginRegistrationGuidance, subject)}";
        }

        private static BsonDocument BuildIndexDiagnostics(
            ICustomIndexStrategyRegistry registry,
            ILitePluginContext pluginContext = null,
            string collection = null,
            string index = null,
            string strategyKind = null,
            string operation = null,
            string @event = null)
        {
            var registeredStrategies = registry?.Registered?.Select(x => x.StrategyId) ?? Enumerable.Empty<string>();

            var diagnostics = new BsonDocument
            {
                ["event"] = @event ?? "plugin.index_required",
                ["operation"] = operation ?? string.Empty,
                ["strategyKind"] = strategyKind ?? DefaultStrategyKind,
                ["collection"] = collection ?? string.Empty,
                ["index"] = index ?? string.Empty,
                ["pluginContextAvailable"] = pluginContext != null || registry != null,
                ["registeredStrategies"] = new BsonArray(registeredStrategies.Select(x => new BsonValue(x)))
            };

            return diagnostics;
        }
    }
}
