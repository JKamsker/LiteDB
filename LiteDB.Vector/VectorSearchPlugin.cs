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

            context.Indexes.Register(new VectorIndexStrategy(context.Logger));

            context.Expressions.RegisterBinaryOperator(new BinaryOperatorRegistration(
                "VECTOR_SIM",
                " VECTOR_SIM ",
                BsonExpressionType.VectorSim,
                VectorExpressions.VectorSimOperator,
                precedence: 2));

            context.Expressions.RegisterFunction(
                "VECTOR_SIM",
                (Func<BsonDocument, Collation, BsonDocument, BsonValue, BsonValue, BsonValue>)VectorExpressions.VectorSimFunction);

            context.Expressions.RegisterKeyword("VECTOR_SIM");

            context.Logger.Write(LogLevel.Information, "VectorSearchPlugin initialized.");
        }
    }
}
