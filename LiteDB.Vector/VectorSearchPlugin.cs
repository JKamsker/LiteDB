using System;
using System.Reflection;
using LiteDB.Plugins;
using LiteDB;

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

            var operatorMethod = typeof(VectorExpressionMethods).GetMethod(nameof(VectorExpressionMethods.VECTOR_SIM), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            context.Expressions.RegisterBinaryOperator(
                name: "VECTOR_SIM",
                displayToken: " VECTOR_SIM ",
                method: operatorMethod,
                expressionType: BsonExpressionType.VectorSim,
                precedence: 9);

            var functionMethod = typeof(VectorExpressionFunctions).GetMethod(nameof(VectorExpressionFunctions.VECTOR_SIM), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            context.Expressions.RegisterFunction(
                name: "VECTOR_SIM",
                method: functionMethod,
                parameterCount: 0,
                expressionType: BsonExpressionType.VectorSim,
                convertScalarLeftToEnumerable: false,
                isScalarResult: true);

            context.Expressions.RegisterKeyword("VECTOR_SIM");

            context.Logger.Write(LogLevel.Information, "VectorSearchPlugin initialized.");

        }
    }
}
