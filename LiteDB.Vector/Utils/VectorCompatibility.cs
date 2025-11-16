using System;

namespace LiteDB.Vector.Utils
{
    /// <summary>
    /// Provides compatibility and error handling utilities for vector operations.
    /// </summary>
    public static class VectorCompatibility
    {
        /// <summary>
        /// Creates a LiteException indicating that the vector plugin is required for an operation.
        /// </summary>
        /// <param name="operation">The operation that requires the plugin.</param>
        /// <param name="diagnostics">Optional diagnostic information.</param>
        /// <returns>A LiteException with error code LITE2002.</returns>
        public static LiteException PluginRequired(string operation, BsonDocument? diagnostics = null)
        {
            var message = $"LiteDB.Vector plugin is required for operation '{operation}'. " +
                         "Install the LiteDB.Vector package and register VectorSearchPlugin during database initialization.";

            var exception = new LiteException(2002, message)
            {
                Data =
                {
                    ["PluginId"] = "LiteDB.Vector",
                    ["Operation"] = operation,
                    ["ErrorCode"] = "LITE2002"
                }
            };

            if (diagnostics != null)
            {
                exception.Data["Diagnostics"] = diagnostics;
            }

            return exception;
        }

        /// <summary>
        /// Creates a LiteException indicating that a legacy vector index needs to be rebuilt.
        /// </summary>
        /// <param name="indexName">The name of the index.</param>
        /// <param name="collection">The collection containing the index.</param>
        /// <returns>A LiteException with error code LITE2002.</returns>
        public static LiteException LegacyIndexNeedsRebuild(string indexName, string collection)
        {
            var message = $"Vector index '{indexName}' in collection '{collection}' uses a prerelease format. " +
                         "Drop and recreate the index using the GA release with the LiteDB.Vector plugin registered.";

            return new LiteException(2002, message)
            {
                Data =
                {
                    ["PluginId"] = "LiteDB.Vector",
                    ["IndexName"] = indexName,
                    ["Collection"] = collection,
                    ["ErrorCode"] = "LITE2002",
                    ["Remediation"] = "Drop the index using the final prerelease build, upgrade to GA, install LiteDB.Vector, and recreate the index."
                }
            };
        }

        /// <summary>
        /// Creates a LiteException for when vector functionality is accessed without the plugin.
        /// </summary>
        /// <param name="featureName">The vector feature being accessed.</param>
        /// <returns>A LiteException with error code LITE2002.</returns>
        public static LiteException FeatureNotAvailable(string featureName)
        {
            var message = $"Vector feature '{featureName}' is not available. " +
                         "The LiteDB.Vector plugin must be registered to use vector search capabilities.";

            return new LiteException(2002, message)
            {
                Data =
                {
                    ["PluginId"] = "LiteDB.Vector",
                    ["Feature"] = featureName,
                    ["ErrorCode"] = "LITE2002",
                    ["Solution"] = "Register VectorSearchPlugin.Instance during database initialization."
                }
            };
        }
    }
}
