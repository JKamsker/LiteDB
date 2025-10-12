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

            var operatorMethod = typeof(VectorExpressionMethods).GetMethod(nameof(VectorExpressionMethods.VectorSim));
            var functionMethod = typeof(VectorExpressionFunctions).GetMethod(nameof(VectorExpressionFunctions.VectorSim));

            if (operatorMethod == null)
            {
                throw new InvalidOperationException("Unable to locate vector expression operator implementation.");
            }

            if (functionMethod == null)
            {
                throw new InvalidOperationException("Unable to locate vector expression function implementation.");
            }

            context.Expressions.RegisterOperator(
                "VECTOR_SIM",
                operatorMethod,
                BsonExpressionType.VectorSim,
                ExpressionPrecedence.Vector,
                " VECTOR_SIM ");

            context.Expressions.RegisterFunction(
                "VECTOR_SIM",
                functionMethod,
                BsonExpressionType.VectorSim,
                convertScalarLeftToEnumerable: false,
                isScalarResult: true);

            context.Logger.Write(LogLevel.Information, "VectorSearchPlugin initialized.");
        }
    }
}
