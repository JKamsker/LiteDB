using LiteDB;
using LiteDB.Vector;
using LiteDB.Plugins;

namespace LiteDB.Vector.Utils
{
    internal sealed class VectorPluginDiagnosticPolicy : PluginDiagnosticPolicy
    {
        public static VectorPluginDiagnosticPolicy Instance { get; } = new VectorPluginDiagnosticPolicy();

        private VectorPluginDiagnosticPolicy()
            : base(PluginMissingBehavior.RefuseDatabase)
        {
        }

        public override LiteException CreateMissingPluginException(string pluginId, string operation, BsonDocument diagnostics)
        {
            var resolvedPluginId = string.IsNullOrWhiteSpace(pluginId) ? VectorPlugin.PluginId : pluginId;
            var enrichedDiagnostics = diagnostics != null ? new BsonDocument(diagnostics) : new BsonDocument();

            enrichedDiagnostics["pluginId"] = resolvedPluginId;

            return VectorCompatibility.CreateMissingPluginException(operation, enrichedDiagnostics);
        }
    }
}
