using System;

namespace LiteDB.Plugins
{
    internal static class PluginPolicyResolver
    {
        public static PluginMissingBehavior ResolveMissingPluginBehavior(ILitePluginContext context)
        {
            if (context is DefaultPluginContext defaultContext)
            {
                return defaultContext.MissingPluginBehavior;
            }

            return PluginMissingBehavior.RefuseDatabase;
        }

        public static PluginMissingBehavior NormalizeMissingPluginBehavior(PluginMissingBehavior behavior)
        {
            return behavior == PluginMissingBehavior.RefuseOperations
                ? PluginMissingBehavior.AllowIfSafe
                : behavior;
        }
    }
}

