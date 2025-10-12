using System;
using LiteDB.Plugins;
using LiteDB.Vector.Expressions;

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

            context.Expressions.RegisterBinaryOperator(
                "VECTOR_SIM",
                VectorExpressions.VectorSimilarity,
                BsonExpressionType.VectorSim,
                precedence: 5,
                source: " VECTOR_SIM ");

            context.Expressions.RegisterFunction(
                "VECTOR_SIM",
                VectorExpressions.VectorSimilarityFunction,
                BsonExpressionType.VectorSim,
                convertScalarLeftToEnumerable: false,
                isScalarResult: true);

            context.Logger.Write(LogLevel.Information, "VectorSearchPlugin initialized.");
        }
    }
}
