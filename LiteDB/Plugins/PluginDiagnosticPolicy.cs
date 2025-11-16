using System;

namespace LiteDB.Plugins
{
    /// <summary>
    /// Policy determining how LiteDB handles missing plugin dependencies.
    /// Core invokes this whenever it encounters plugin-owned assets without the plugin loaded.
    /// </summary>
    public interface IPluginDiagnosticPolicy
    {
        /// <summary>
        /// Gets the behavior to use when plugin-owned assets are detected without the plugin.
        /// </summary>
        PluginMissingBehavior MissingBehavior { get; }

        /// <summary>
        /// Creates an exception to throw when a missing plugin is encountered.
        /// </summary>
        /// <param name="pluginId">The identifier of the missing plugin.</param>
        /// <param name="operation">The operation that requires the plugin.</param>
        /// <param name="diagnostics">Diagnostic information to include in the exception.</param>
        /// <returns>A LiteException with appropriate error code and message.</returns>
        LiteException CreateMissingPluginException(string pluginId, string operation, BsonDocument diagnostics);
    }

    /// <summary>
    /// Defines how the database behaves when plugin-owned assets are detected without the plugin.
    /// </summary>
    public enum PluginMissingBehavior
    {
        /// <summary>
        /// Refuse to open the database entirely.
        /// Use when plugin-owned assets are unsafe to ignore (e.g., corrupt critical metadata).
        /// </summary>
        RefuseDatabase = 0,

        /// <summary>
        /// Open the database but throw exceptions when plugin-owned assets are accessed.
        /// Non-plugin collections remain fully functional.
        /// This is the default behavior for most plugins.
        /// </summary>
        RefuseOperations = 1,

        /// <summary>
        /// Allow database to open and defer to runtime safety checks.
        /// Only use when plugin absence can be safely detected per-operation.
        /// Maps to "Safe To Ignore = Yes" in the behavior matrix.
        /// </summary>
        AllowIfSafe = 2
    }

    /// <summary>
    /// Default implementation of the plugin diagnostic policy.
    /// Uses RefuseOperations behavior and standard LiteDB error codes.
    /// </summary>
    public sealed class DefaultPluginDiagnosticPolicy : IPluginDiagnosticPolicy
    {
        private readonly PluginMissingBehavior _behavior;

        /// <summary>
        /// Initializes a new default diagnostic policy.
        /// </summary>
        /// <param name="behavior">The missing behavior to use (defaults to RefuseOperations).</param>
        public DefaultPluginDiagnosticPolicy(PluginMissingBehavior behavior = PluginMissingBehavior.RefuseOperations)
        {
            _behavior = behavior;
        }

        /// <summary>
        /// Gets the default instance using RefuseOperations behavior.
        /// </summary>
        public static DefaultPluginDiagnosticPolicy Instance { get; } = new DefaultPluginDiagnosticPolicy();

        /// <inheritdoc/>
        public PluginMissingBehavior MissingBehavior => _behavior;

        /// <inheritdoc/>
        public LiteException CreateMissingPluginException(string pluginId, string operation, BsonDocument diagnostics)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
                throw new ArgumentException("Plugin ID cannot be null or whitespace.", nameof(pluginId));

            if (string.IsNullOrWhiteSpace(operation))
                throw new ArgumentException("Operation cannot be null or whitespace.", nameof(operation));

            var message = $"Plugin '{pluginId}' is required for operation '{operation}' but is not registered. " +
                         $"Install the {pluginId} package and register the plugin during database initialization.";

            var exception = new LiteException(2002, message)
            {
                Data =
                {
                    ["PluginId"] = pluginId,
                    ["Operation"] = operation,
                    ["ErrorCode"] = "LITE2002"
                }
            };

            if (diagnostics != null && diagnostics.Count > 0)
            {
                exception.Data["Diagnostics"] = diagnostics;
            }

            return exception;
        }
    }
}
