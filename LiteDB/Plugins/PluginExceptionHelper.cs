using System;

namespace LiteDB.Plugins
{
    internal static class PluginExceptionHelper
    {
        public static LiteException PluginRequired(string pluginId)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentNullException(nameof(pluginId));
            }

            return new LiteException(LiteException.PLUGIN_REQUIRED, $"Plugin '{pluginId}' is required for this operation. Install the plugin package and register it via LiteDatabaseOptions.Plugins.");
        }
    }
}
