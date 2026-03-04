using System;

namespace LiteDB.Plugins
{
    internal static class PluginExceptionHelper
    {
        public static LiteException PluginRequired(string pluginId, string message = null)
        {
            var resolvedId = string.IsNullOrWhiteSpace(pluginId) ? null : pluginId;
            var text = string.IsNullOrWhiteSpace(message)
                ? (resolvedId == null
                    ? "A plugin is required for this operation. Install the appropriate package and register it via LiteDatabaseOptions.Plugins."
                    : $"Plugin '{resolvedId}' is required for this operation. Install the plugin package and register it via LiteDatabaseOptions.Plugins.")
                : message;

            return new LiteException(LiteException.PLUGIN_REQUIRED, text);
        }
    }
}
