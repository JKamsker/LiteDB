using System;
using LiteDB;
using LiteDB.Plugins;

namespace LiteDB.Vector
{
    /// <summary>
    /// Provides vector search capabilities for <see cref="LiteDatabase"/> instances via the plugin pipeline.
    /// </summary>
    public sealed class VectorSearchPlugin : ILitePlugin
    {
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

            context.Expressions.RegisterOperator(
                token: "VECTOR_SIM",
                expression: " VECTOR_SIM ",
                operation: BsonExpressionOperators.VECTOR_SIM,
                type: BsonExpressionType.VectorSim,
                precedence: 5);

            context.Indexes.Register(VectorIndexStrategy.Instance);

            context.Logger.Write(LogLevel.Information, "VectorSearchPlugin initialized.");
        }
    }
}
