using System;
using LiteDB;

namespace LiteDB.Plugins
{
    /// <summary>
    /// Defines how the database reacts when plugin-owned assets are encountered but the owning plugin is missing.
    /// </summary>
    public interface IPluginDiagnosticPolicy
    {
        [Obsolete("Use LiteDatabaseOptions.MissingPluginBehavior instead. This property is ignored for enforcement.")]
        PluginMissingBehavior MissingBehavior { get; }

        LiteException CreateMissingPluginException(string pluginId, string operation, BsonDocument diagnostics);
    }

    /// <summary>
    /// Supported missing-plugin behaviors.
    /// </summary>
    public enum PluginMissingBehavior
    {
        RefuseDatabase = 0,
        [Obsolete("Use AllowIfSafe instead. RefuseOperations is treated as AllowIfSafe.")]
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

        [Obsolete("Use LiteDatabaseOptions.MissingPluginBehavior instead. This property is ignored for enforcement.")]
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
            : base(PluginMissingBehavior.RefuseDatabase)
        {
        }

        public override LiteException CreateMissingPluginException(string pluginId, string operation, BsonDocument diagnostics)
        {
            var resolvedPluginId = ResolvePluginId(pluginId, diagnostics);
            var resolvedOperation = operation ?? "the requested operation";
            var message = BuildMessage(resolvedPluginId, resolvedOperation);

            var exception = new LiteException(LiteException.PLUGIN_REQUIRED, message);

            if (diagnostics != null)
            {
                try
                {
                    exception.Data["PluginDiagnostics"] = DefaultPluginContext.CloneDiagnostics(diagnostics);
                }
                catch (ArgumentException)
                {
                    exception.Data["PluginDiagnostics"] = diagnostics.ToString();
                }
            }

            return exception;
        }

        private static string ResolvePluginId(string pluginId, BsonDocument diagnostics)
        {
            if (!string.IsNullOrWhiteSpace(pluginId))
            {
                return pluginId;
            }

            if (diagnostics != null &&
                diagnostics.TryGetValue("pluginId", out var value) &&
                value.IsString &&
                !string.IsNullOrWhiteSpace(value.AsString))
            {
                return value.AsString;
            }

            return null;
        }

        private static string BuildMessage(string pluginId, string operation)
        {
            var hasPluginId = !string.IsNullOrWhiteSpace(pluginId);
            var subject = hasPluginId ? $"Plugin '{pluginId}'" : "A plugin";
            return $"{subject} is required to perform '{operation}'. Install and register the plugin to continue.";
        }
    }
}
