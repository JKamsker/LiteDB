using System;
using System.Globalization;
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

            var defaultMetric = TryReadDefaultMetric(context.ConnectionString["vector.metric"], context.Logger);

            context.Expressions.RegisterKeyword("VECTOR_SIM");
            context.Expressions.RegisterBinaryOperator(
                "VECTOR_SIM",
                BsonExpressionType.VectorSim,
                VectorExpressions.VectorSimilarity,
                BinaryOperatorPrecedence.Comparison,
                " VECTOR_SIM ");
            context.Expressions.RegisterFunction(
                "VECTOR_SIM",
                new Func<BsonDocument, Collation, BsonDocument, BsonValue, BsonValue, BsonValue>(VectorExpressions.VectorSimilarity),
                BsonExpressionType.VectorSim,
                convertScalarLeftToEnumerable: false,
                isScalarResult: true);

            context.Indexes.Register(new VectorIndexStrategy(context.Logger, defaultMetric));

            context.Logger.Write(LogLevel.Information, "VectorSearchPlugin initialized.");

        }

        private static VectorDistanceMetric? TryReadDefaultMetric(string value, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (Enum.TryParse<VectorDistanceMetric>(value, true, out var parsed))
            {
                logger.Write(LogLevel.Information, $"Using '{parsed}' as the default vector distance metric from the connection string.");
                return parsed;
            }

            if (byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric) && Enum.IsDefined(typeof(VectorDistanceMetric), numeric))
            {
                var metric = (VectorDistanceMetric)numeric;
                logger.Write(LogLevel.Information, $"Using '{metric}' as the default vector distance metric from the connection string.");
                return metric;
            }

            logger.Write(LogLevel.Warning, $"Unrecognized vector.metric value '{value}'. Falling back to explicit index configuration.");
            return null;
        }
    }
}
