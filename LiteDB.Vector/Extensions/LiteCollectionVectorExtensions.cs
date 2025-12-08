using System;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Plugins.Indexing;
using LiteDB.Vector.Utils;

namespace LiteDB.Vector
{
    /// <summary>
    /// Extension methods that expose vector-aware index creation for <see cref="ILiteCollection{T}"/>.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// var options = new VectorIndexOptions(384);
    /// collection.EnsureIndex("embedding_idx", x => x.Embedding, options);
    /// ]]></code>
    /// </example>
    public static class LiteCollectionVectorExtensions
    {
        /// <summary>
        /// Creates a vector index with an explicit name using a <see cref="BsonExpression"/>.
        /// </summary>
        /// <typeparam name="T">Document type managed by the collection.</typeparam>
        /// <param name="collection">Collection to index.</param>
        /// <param name="name">Index name.</param>
        /// <param name="expression">Field expression pointing to the vector data.</param>
        /// <param name="options">Vector index configuration.</param>
        /// <returns><c>true</c> when a new index is created; otherwise <c>false</c>.</returns>
        /// <example>
        /// <code><![CDATA[
        /// var options = new VectorIndexOptions(384);
        /// var created = collection.EnsureIndex("embedding_idx", BsonExpression.Create("$.Embedding"), options);
        /// ]]></code>
        /// </example>
        public static bool EnsureIndex<T>(this ILiteCollection<T> collection, string name, BsonExpression expression, VectorIndexOptions options)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            if (options == null) throw new ArgumentNullException(nameof(options));

            return EnsureVectorIndex(
                Unwrap(collection),
                name,
                expression,
                options);
        }

        /// <summary>
        /// Creates a vector index using a <see cref="BsonExpression"/> with an auto-generated name.
        /// </summary>
        /// <typeparam name="T">Document type managed by the collection.</typeparam>
        /// <param name="collection">Collection to index.</param>
        /// <param name="expression">Field expression pointing to the vector data.</param>
        /// <param name="options">Vector index configuration.</param>
        /// <returns><c>true</c> when a new index is created; otherwise <c>false</c>.</returns>
        /// <example>
        /// <code><![CDATA[
        /// collection.EnsureIndex(BsonExpression.Create("$.Embedding"), new VectorIndexOptions(384));
        /// ]]></code>
        /// </example>
        public static bool EnsureIndex<T>(this ILiteCollection<T> collection, BsonExpression expression, VectorIndexOptions options)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var generatedName = Regex.Replace(expression.Source, @"[^a-z0-9]", "", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            return EnsureVectorIndex(
                Unwrap(collection),
                generatedName,
                expression,
                options);
        }

        /// <summary>
        /// Creates a vector index using a strongly-typed lambda expression with an auto-generated name.
        /// </summary>
        /// <typeparam name="T">Document type managed by the collection.</typeparam>
        /// <typeparam name="K">Property type returned by the selector.</typeparam>
        /// <param name="collection">Collection to index.</param>
        /// <param name="keySelector">Lambda selecting the vector property.</param>
        /// <param name="options">Vector index configuration.</param>
        /// <returns><c>true</c> when a new index is created; otherwise <c>false</c>.</returns>
        /// <example>
        /// <code><![CDATA[
        /// collection.EnsureIndex(x => x.Embedding, new VectorIndexOptions(384));
        /// ]]></code>
        /// </example>
        public static bool EnsureIndex<T, K>(this ILiteCollection<T> collection, Expression<Func<T, K>> keySelector, VectorIndexOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            var concrete = Unwrap(collection);
            var expression = concrete.GetIndexExpression(keySelector, convertEnumerableToMultiKey: false);
            var generatedName = Regex.Replace(expression.Source, @"[^a-z0-9]", "", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            return EnsureVectorIndex(
                concrete,
                generatedName,
                expression,
                options);
        }

        /// <summary>
        /// Creates a vector index using a strongly-typed lambda expression and an explicit name.
        /// </summary>
        /// <typeparam name="T">Document type managed by the collection.</typeparam>
        /// <typeparam name="K">Property type returned by the selector.</typeparam>
        /// <param name="collection">Collection to index.</param>
        /// <param name="name">Index name.</param>
        /// <param name="keySelector">Lambda selecting the vector property.</param>
        /// <param name="options">Vector index configuration.</param>
        /// <returns><c>true</c> when a new index is created; otherwise <c>false</c>.</returns>
        /// <example>
        /// <code><![CDATA[
        /// collection.EnsureIndex("embedding_idx", x => x.Embedding, new VectorIndexOptions(384));
        /// ]]></code>
        /// </example>
        public static bool EnsureIndex<T, K>(this ILiteCollection<T> collection, string name, Expression<Func<T, K>> keySelector, VectorIndexOptions options)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var concrete = Unwrap(collection);
            var expression = concrete.GetIndexExpression(keySelector, convertEnumerableToMultiKey: false);

            return EnsureVectorIndex(
                concrete,
                name,
                expression,
                options);
        }

        private static LiteCollection<T> Unwrap<T>(ILiteCollection<T> collection)
        {
            if (collection is null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            if (collection is LiteCollection<T> concrete)
            {
                return concrete;
            }

            throw new ArgumentException("Vector index operations require LiteDB's default collection implementation.", nameof(collection));
        }

        private static bool EnsureVectorIndex<T>(LiteCollection<T> collection, string name, BsonExpression expression, VectorIndexOptions options)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var database = collection.Database;
            var services = database?.Services;
            var descriptor = VectorCompatibility.TryGetStrategy(services?.CustomIndexes);
            var pluginContext = services?.Context;

            if (descriptor == null || pluginContext == null)
            {
                throw CreateMissingPluginException(collection, pluginContext);
            }

            var metadataDescriptor = RequireMetadataDescriptor(pluginContext);
            var materializedOptions = VectorExtensionHelpers.CreateOptionsDocument(options, metadataDescriptor);

            var ensureContext = new EnsureIndexContext(
                database,
                collection.Engine as LiteEngine,
                typeof(T),
                collection.Name,
                name,
                expression,
                unique: false,
                collection.Mapper,
                pluginContext,
                (indexName, indexExpression, _) => collection.Engine.EnsureCustomIndex(collection.Name, indexName, VectorCompatibility.DefaultStrategyKind, indexExpression, materializedOptions));

            var vectorContext = new CustomIndexEnsureContext(ensureContext, materializedOptions);

            return descriptor.EnsureIndex(vectorContext);
        }

        private static PluginIndexMetadataDescriptor RequireMetadataDescriptor(ILitePluginContext pluginContext)
        {
            if (pluginContext?.IndexMetadata == null)
            {
                throw VectorCompatibility.PluginRequired();
            }

            if (pluginContext.IndexMetadata.TryGet(VectorCompatibility.DefaultIndexKind, out var descriptor))
            {
                return descriptor;
            }

            throw VectorCompatibility.PluginRequired();
        }

        private static LiteException CreateMissingPluginException<T>(LiteCollection<T> collection, ILitePluginContext pluginContext)
        {
            var diagnostics = new BsonDocument
            {
                ["event"] = "plugin.index_required",
                ["operation"] = "EnsureCustomIndex",
                ["strategyKind"] = VectorPlugin.StrategyKind,
                ["collection"] = collection?.Name ?? string.Empty,
                ["pluginContextAvailable"] = pluginContext != null,
                ["registeredStrategies"] = new BsonArray()
            };

            var registry = pluginContext?.CustomIndexes?.Registered;

            if (registry != null && registry.Count > 0)
            {
                var registered = new BsonArray();

                foreach (var strategy in registry)
                {
                    if (strategy == null)
                    {
                        continue;
                    }

                    registered.Add(strategy.StrategyId ?? string.Empty);
                }

                diagnostics["registeredStrategies"] = registered;
            }

            var exception = VectorCompatibility.PluginRequired();
            exception.Data["VectorDiagnostics"] = diagnostics;

            return exception;
        }
    }
}


