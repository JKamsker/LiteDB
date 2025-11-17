using LiteDB;
using LiteDB.Plugins;

namespace LiteDB.Vector.Utils
{
    internal sealed class VectorPluginDiagnosticPolicy : PluginDiagnosticPolicy
    {
        public static VectorPluginDiagnosticPolicy Instance { get; } = new VectorPluginDiagnosticPolicy();

        private VectorPluginDiagnosticPolicy()
            : base(PluginMissingBehavior.RefuseOperations)
        {
        }

        public override LiteException CreateMissingPluginException(string pluginId, string operation, BsonDocument diagnostics)
        {
            var resolvedPluginId = string.IsNullOrWhiteSpace(pluginId) ? ReservedCodeRanges.VectorPluginId : pluginId;
            var message = $"Vector search requires the LiteDB.Vector plugin. Install the LiteDB.Vector package and register VectorSearchPlugin.Instance (for example, new LiteDatabase(connectionString, plugins: new[] {{ VectorSearchPlugin.Instance }})) before performing '{operation ?? "the requested operation"}'.";

            var exception = new LiteException(LiteException.PLUGIN_REQUIRED, message);

            if (diagnostics != null)
            {
                try
                {
                    diagnostics["pluginId"] = resolvedPluginId;
                    exception.Data["PluginDiagnostics"] = diagnostics;
                }
                catch (System.ArgumentException)
                {
                    exception.Data["PluginDiagnostics"] = diagnostics.ToString();
                }
            }

            return exception;
        }
    }
}
