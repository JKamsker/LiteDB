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
            var message = $"Vector index support requires the LiteDB.Vector plugin. Install the LiteDB.Vector package and register VectorSearchPlugin.Instance (for example, new LiteDatabase(connectionString, plugins: new[] {{ VectorSearchPlugin.Instance }})) before performing '{operation ?? "the requested operation"}'.";

            var exception = new LiteException(LiteException.PLUGIN_REQUIRED, message);

            if (diagnostics != null)
            {
                try
                {
                    diagnostics["pluginId"] = resolvedPluginId;
                    exception.Data["PluginDiagnostics"] = diagnostics;
                    exception.Data["VectorDiagnostics"] = diagnostics;
                }
                catch (System.ArgumentException)
                {
                    exception.Data["PluginDiagnostics"] = diagnostics.ToString();
                    exception.Data["VectorDiagnostics"] = diagnostics.ToString();
                }
            }

            return exception;
        }
    }
}
