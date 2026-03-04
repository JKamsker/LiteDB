using System;
using LiteDB.Plugins;

namespace LiteDB.Vector.Utils
{
    /// <summary>
    /// Provides telemetry helpers used to surface compatibility issues and remediation guidance.
    /// </summary>
    internal static class VectorTelemetry
    {
        private const string UpgradeGuidance = "Verify LiteDB and LiteDB.Vector packages are aligned and run scripts/vector/Invoke-VectorUpgrade.ps1 to finalize the migration.";

        /// <summary>
        /// Validates that the supplied plugin context exposes all vector-specific registries.
        /// Emits telemetry when required registries are absent so operators can remediate the deployment.
        /// </summary>
        /// <param name="context">Plugin context provided during initialization.</param>
        /// <returns><c>true</c> when all prerequisites are available.</returns>
        internal static bool EnsurePluginPrerequisites(ILitePluginContext context)
        {
            if (context == null)
            {
                return false;
            }

            if (context.CustomIndexes == null)
            {
                EmitCompatibilityWarning(
                    context.Logger,
                    "vector.registry.unavailable",
                    "LiteDB core did not expose the vector index strategy registry. Vector operations will remain disabled until the host is upgraded.");
                return false;
            }

            if (context.PageFactories == null)
            {
                EmitCompatibilityWarning(
                    context.Logger,
                    "storage.registry.unavailable",
                    "LiteDB core did not expose the plugin page factory registry required by vector indexes.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Emits a structured compatibility warning through the plugin logger.
        /// </summary>
        internal static void EmitCompatibilityWarning(ILogger logger, string code, string detail, Exception exception = null)
        {
            if (logger == null)
            {
                return;
            }

            logger.Write(
                LogLevel.Warning,
                $"[LiteDB.Vector:{code}] {detail} Remediation: {UpgradeGuidance}",
                exception);
        }

        /// <summary>
        /// Logs a failure that occurred during plugin initialization with remediation hints.
        /// </summary>
        internal static void EmitInitializationFailure(ILogger logger, Exception exception)
        {
            EmitCompatibilityWarning(
                logger,
                "plugin.initialize.failed",
                "VectorSearchPlugin failed to initialize against the current LiteDB build.",
                exception);
        }
    }
}

