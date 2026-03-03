using System;
using LiteDB.Plugins;

namespace LiteDB.Vector
{
    /// <summary>
    /// Provides vector search capabilities for <see cref="LiteDatabase"/> instances via the plugin pipeline.
    /// </summary>
    public sealed class VectorSearchPlugin : ILitePlugin
    {
        private bool _initialized;

        /// <summary>
        /// Gets the singleton instance used when enabling the plugin via configuration.
        /// </summary>
        public static VectorSearchPlugin Instance { get; } = new VectorSearchPlugin();

        private VectorSearchPlugin()
        {
        }

        /// <inheritdoc />
        public void Initialize(LiteDatabase database, ILitePluginContext context)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (_initialized)
            {
                context.Logger.Write(LogLevel.Debug, "VectorSearchPlugin already initialized for this database instance.");
                return;
            }

            context.Indexes.Register(new VectorIndexStrategy(context.Logger));

            context.Logger.Write(LogLevel.Information, "VectorSearchPlugin initialized.");

            _initialized = true;
        }
    }
}
