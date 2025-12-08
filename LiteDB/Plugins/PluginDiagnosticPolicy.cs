using System;
using LiteDB;

namespace LiteDB.Plugins
{
    /// <summary>
    /// Defines how the database reacts when plugin-owned assets are encountered but the owning plugin is missing.
    /// </summary>
    public interface IPluginDiagnosticPolicy
    {
        PluginMissingBehavior MissingBehavior { get; }

        LiteException CreateMissingPluginException(string pluginId, string operation, BsonDocument diagnostics);
    }

    /// <summary>
    /// Supported missing-plugin behaviors.
    /// </summary>
    public enum PluginMissingBehavior
    {
        RefuseDatabase = 0,
        RefuseOperations = 1,
        AllowIfSafe = 2
    }

    /// <summary>
    /// Base class that provides common helpers for plugin diagnostic policies.
    /// </summary>
    public abstract class PluginDiagnosticPolicy : IPluginDiagnosticPolicy
    {
        protected PluginDiagnosticPolicy(PluginMissingBehavior behavior)
        {
            MissingBehavior = behavior;
        }

        public PluginMissingBehavior MissingBehavior { get; }

        public abstract LiteException CreateMissingPluginException(string pluginId, string operation, BsonDocument diagnostics);
    }

    /// <summary>
    /// Default policy used when plugins do not register a custom implementation.
    /// </summary>
    public sealed class DefaultPluginDiagnosticPolicy : PluginDiagnosticPolicy
    {
        public static IPluginDiagnosticPolicy Instance { get; } = new DefaultPluginDiagnosticPolicy();

        private DefaultPluginDiagnosticPolicy()
            : base(PluginMissingBehavior.RefuseOperations)
        {
        }

        public override LiteException CreateMissingPluginException(string pluginId, string operation, BsonDocument diagnostics)
        {
            var hasPluginId = !string.IsNullOrWhiteSpace(pluginId);
            var subject = hasPluginId ? $"Plugin '{pluginId}'" : "A plugin";
            var message = $"{subject} is required to perform '{operation ?? "the requested operation"}'. " +
                          "Install and register the plugin to continue.";

            var exception = new LiteException(LiteException.PLUGIN_REQUIRED, message);

            if (diagnostics != null)
            {
                try
                {
                    exception.Data["PluginDiagnostics"] = diagnostics;
                }
                catch (ArgumentException)
                {
                    exception.Data["PluginDiagnostics"] = diagnostics.ToString();
                }
            }

            return exception;
        }
    }
}
