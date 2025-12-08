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
            var message = VectorCompatibility.BuildMissingPluginMessage(operation);

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
