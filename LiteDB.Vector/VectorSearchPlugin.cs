using System;
using System.Globalization;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Plugins.Indexing;
using LiteDB.Plugins.Query;
using LiteDB.Plugins.Storage;
using LiteDB.Vector.Engine;
using LiteDB.Vector.Query;
using LiteDB.Vector.Utils;

namespace LiteDB.Vector
{
    /// <summary>
    /// Provides vector search capabilities for <see cref="LiteDatabase"/> instances via the plugin pipeline.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// using var db = new LiteDatabase(
    ///     "Filename=mydata.db",
    ///     plugins: new[] { VectorSearchPlugin.Instance });
    /// ]]></code>
    /// </example>
    public sealed class VectorSearchPlugin : ILitePlugin
    {
        /// <summary>
        /// Gets the singleton instance used when enabling the plugin via configuration.
        /// </summary>
        /// <example>
        /// <code><![CDATA[
        /// var db = new LiteDatabase("Filename=my.db", plugins: new[] { VectorSearchPlugin.Instance });
        /// ]]></code>
        /// </example>
        public static VectorSearchPlugin Instance { get; } = new VectorSearchPlugin();

        private VectorSearchPlugin()
        {
        }

        /// <summary>
        /// Registers vector-aware index strategies, query planning rules, and expression support.
        /// </summary>
        /// <param name="database">Database instance that will host vector operations.</param>
        /// <param name="context">Plugin context used to register expressions and services.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="database"/> or <paramref name="context"/> is <c>null</c>.</exception>
        /// <example>
        /// <code><![CDATA[
        /// var db = new LiteDatabase("Filename=my.db", plugins: new[] { VectorSearchPlugin.Instance });
        /// ]]></code>
        /// </example>
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

            if (!VectorTelemetry.EnsurePluginPrerequisites(context))
            {
                return;
            }

            try
            {
                var defaultMetric = TryReadDefaultMetric(context.ConnectionString["vector.metric"], context.Logger);

                context.RegisterQueryMetadata(
                    pluginId: VectorQueryMetadata.PluginId,
                    version: VectorQueryMetadata.Version,
                    reservedKeys: VectorQueryMetadata.ReservedKeys);

                VectorIndexServiceFactory.Register((snapshot, collation) => new VectorIndexSearchAdapter(snapshot, collation));

                context.Expressions.RegisterKeyword("VECTOR_DIST");
                context.Expressions.RegisterBinaryOperator(
                    "VECTOR_DIST",
                    BsonExpressionType.VectorDist,
                    VectorExpressions.VectorDistance,
                    BinaryOperatorPrecedence.Comparison,
                    " VECTOR_DIST "); // Comparison precedence keeps distance checks aligned with relational operators.
                context.Expressions.RegisterFunction(
                    "VECTOR_DIST",
                    new Func<BsonDocument, Collation, BsonDocument, BsonValue, BsonValue, BsonValue>(VectorExpressions.VectorDistance),
                    BsonExpressionType.VectorDist,
                    convertScalarLeftToEnumerable: false,
                    isScalarResult: true);
                context.Expressions.RegisterFunction(
                    "VECTOR_DIST",
                    new Func<BsonDocument, Collation, BsonDocument, BsonValue, BsonValue, BsonValue, BsonValue>(VectorExpressions.VectorDistance),
                    BsonExpressionType.VectorDist,
                    convertScalarLeftToEnumerable: false,
                    isScalarResult: true);

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
                context.Expressions.RegisterFunction(
                    "VECTOR_SIM",
                    new Func<BsonDocument, Collation, BsonDocument, BsonValue, BsonValue, BsonValue, BsonValue>(VectorExpressions.VectorSimilarity),
                    BsonExpressionType.VectorSim,
                    convertScalarLeftToEnumerable: false,
                    isScalarResult: true);

                var vectorIndexStrategy = new VectorIndexStrategy(context.Logger, defaultMetric);
                context.Indexes.Register(vectorIndexStrategy);
                context.QueryPlanner.AddRule(new VectorIndexPlanningRule());

                context.Logger.Write(LogLevel.Information, "VectorSearchPlugin initialized.");

                context.RegisterPageFactory(new PageFactoryRegistration(
                    pluginId: "LiteDB.Vector",
                    pageType: "VectorIndex",
                    compatibilityRange: ">=8.0",
                    factory: ctx =>
                    {
                        if (ctx is PageConstructionContext construction)
                        {
                            return construction.IsNewPage
                                ? new VectorIndexPage(construction.Buffer, construction.PageId)
                                : new VectorIndexPage(construction.Buffer);
                        }

                        throw new ArgumentException("Vector index page factory received an unexpected context instance.", nameof(ctx));
                    }));

#pragma warning disable CS0618
                var descriptor = new CustomIndexStrategyDescriptor(
                    pluginId: "LiteDB.Vector",
                    strategyId: "LiteDB.Vector",
                    ensureIndex: ctx =>
                    {
                        if (ctx == null)
                        {
                            throw new ArgumentNullException(nameof(ctx));
                        }

                        var result = ctx.EnsureContext.ExecuteDefault();
                        ctx.EnsureContext.SetResult(result);
                        return result;
                    },
                    queryPlanner: _ => { },
                    rebuildStrategy: _ => { },
                    requiredBsonTypes: new[] { (byte)BsonType.Vector },
                    requiredPageTypes: new[] { "VectorIndex" });
#pragma warning restore CS0618

                context.RegisterCustomIndexStrategy(descriptor);
            }
            catch (Exception ex)
            {
                VectorTelemetry.EmitInitializationFailure(context.Logger, ex);
                throw;
            }
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


