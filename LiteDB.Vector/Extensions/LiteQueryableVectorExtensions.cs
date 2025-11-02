using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace LiteDB.Vector
{
    /// <summary>
    /// Extension methods that surface vector-aware query capabilities for <see cref="ILiteQueryable{T}"/>.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// var matches = collection.Query()
    ///     .TopKNear(x => x.Embedding, queryVector, k: 5)
    ///     .WithVectorScore()
    ///     .ToList();
    /// ]]></code>
    /// </example>
    public static class LiteQueryableVectorExtensions
    {
        /// <summary>
        /// Filters documents where the supplied vector field is within the provided distance threshold.
        /// </summary>
        /// <typeparam name="T">Document type.</typeparam>
        /// <param name="source">Source queryable.</param>
        /// <param name="vectorField">Name of the vector field (e.g., <c>"Embedding"</c>).</param>
        /// <param name="target">Query vector.</param>
        /// <param name="maxDistance">Maximum distance (inclusive).</param>
        /// <param name="metric">Optional metric override when no index is available.</param>
        /// <returns>The filtered queryable.</returns>
        /// <example>
        /// <code><![CDATA[
        /// var near = collection.Query()
        ///     .WhereNear("Embedding", queryVector, maxDistance: 0.6)
        ///     .ToList();
        /// ]]></code>
        /// </example>
        public static ILiteQueryable<T> WhereNear<T>(this ILiteQueryable<T> source, string vectorField, float[] target, double maxDistance, VectorDistanceMetric? metric = null)
        {
            return Unwrap(source).VectorWhereNear(vectorField, target, maxDistance, ToMetricByte(metric));
        }

        /// <summary>
        /// Filters documents using a <see cref="BsonExpression"/> that identifies the vector field.
        /// </summary>
        /// <typeparam name="T">Document type.</typeparam>
        /// <param name="source">Source queryable.</param>
        /// <param name="fieldExpr">Expression selecting the vector field.</param>
        /// <param name="target">Query vector.</param>
        /// <param name="maxDistance">Maximum distance (inclusive).</param>
        /// <param name="metric">Optional metric override when no index is available.</param>
        /// <returns>The filtered queryable.</returns>
        /// <example>
        /// <code><![CDATA[
        /// var expression = BsonExpression.Create("$.Embedding");
        /// var results = collection.Query().WhereNear(expression, queryVector, 0.6).ToEnumerable();
        /// ]]></code>
        /// </example>
        public static ILiteQueryable<T> WhereNear<T>(this ILiteQueryable<T> source, BsonExpression fieldExpr, float[] target, double maxDistance, VectorDistanceMetric? metric = null)
        {
            return Unwrap(source).VectorWhereNear(fieldExpr, target, maxDistance, ToMetricByte(metric));
        }

        /// <summary>
        /// Filters documents using a strongly-typed lambda expression.
        /// </summary>
        /// <typeparam name="T">Document type.</typeparam>
        /// <typeparam name="K">Vector property type.</typeparam>
        /// <param name="source">Source queryable.</param>
        /// <param name="field">Lambda selecting the vector property.</param>
        /// <param name="target">Query vector.</param>
        /// <param name="maxDistance">Maximum distance (inclusive).</param>
        /// <param name="metric">Optional metric override when no index is available.</param>
        /// <returns>The filtered queryable.</returns>
        /// <example>
        /// <code><![CDATA[
        /// var near = collection.Query()
        ///     .WhereNear(x => x.Embedding, queryVector, 0.6)
        ///     .ToList();
        /// ]]></code>
        /// </example>
        public static ILiteQueryable<T> WhereNear<T, K>(this ILiteQueryable<T> source, Expression<Func<T, K>> field, float[] target, double maxDistance, VectorDistanceMetric? metric = null)
        {
            return Unwrap(source).VectorWhereNear(field, target, maxDistance, ToMetricByte(metric));
        }

        /// <summary>
        /// Executes <see cref="WhereNear{T}(ILiteQueryable{T}, string, float[], double, VectorDistanceMetric?)"/> immediately and materializes the results.
        /// </summary>
        /// <typeparam name="T">Document type.</typeparam>
        /// <param name="source">Source queryable.</param>
        /// <param name="vectorField">Name of the vector field.</param>
        /// <param name="target">Query vector.</param>
        /// <param name="maxDistance">Maximum distance (inclusive).</param>
        /// <param name="metric">Optional metric override.</param>
        /// <returns>Enumerated results within the distance threshold.</returns>
        /// <example>
        /// <code><![CDATA[
        /// foreach (var article in collection.Query().FindNearest("Embedding", queryVector, 0.5))
        /// {
        ///     Console.WriteLine(article.Title);
        /// }
        /// ]]></code>
        /// </example>
        public static IEnumerable<T> FindNearest<T>(this ILiteQueryable<T> source, string vectorField, float[] target, double maxDistance, VectorDistanceMetric? metric = null)
        {
            var queryable = Unwrap(source);
            return queryable.VectorWhereNear(vectorField, target, maxDistance, ToMetricByte(metric)).ToEnumerable();
        }

        /// <summary>
        /// Returns the top-k nearest neighbours using a lambda selector.
        /// </summary>
        /// <typeparam name="T">Document type.</typeparam>
        /// <typeparam name="K">Vector property type.</typeparam>
        /// <param name="source">Source queryable.</param>
        /// <param name="field">Lambda selecting the vector property.</param>
        /// <param name="target">Query vector.</param>
        /// <param name="k">Number of matches to return.</param>
        /// <param name="metric">Optional metric override.</param>
        /// <param name="maxDistance">Optional distance guard.</param>
        /// <returns>A queryable containing the nearest matches.</returns>
        /// <example>
        /// <code><![CDATA[
        /// var matches = collection.Query()
        ///     .TopKNear(x => x.Embedding, queryVector, k: 5)
        ///     .ToList();
        /// ]]></code>
        /// </example>
        public static ILiteQueryableResult<T> TopKNear<T, K>(this ILiteQueryable<T> source, Expression<Func<T, K>> field, float[] target, int k, VectorDistanceMetric? metric = null, double? maxDistance = null)
        {
            return Unwrap(source).VectorTopKNear(field, target, k, ToMetricByte(metric), maxDistance);
        }

        /// <summary>
        /// Returns the top-k nearest neighbours using a string field selector.
        /// </summary>
        /// <typeparam name="T">Document type.</typeparam>
        /// <param name="source">Source queryable.</param>
        /// <param name="field">Name of the vector field.</param>
        /// <param name="target">Query vector.</param>
        /// <param name="k">Number of matches to return.</param>
        /// <param name="metric">Optional metric override.</param>
        /// <param name="maxDistance">Optional distance guard.</param>
        /// <returns>A queryable containing the nearest matches.</returns>
        /// <example>
        /// <code><![CDATA[
        /// var matches = collection.Query()
        ///     .TopKNear("Embedding", queryVector, 10, maxDistance: 0.8)
        ///     .ToDocuments();
        /// ]]></code>
        /// </example>
        public static ILiteQueryableResult<T> TopKNear<T>(this ILiteQueryable<T> source, string field, float[] target, int k, VectorDistanceMetric? metric = null, double? maxDistance = null)
        {
            return Unwrap(source).VectorTopKNear(field, target, k, ToMetricByte(metric), maxDistance);
        }

        /// <summary>
        /// Returns the top-k nearest neighbours using a <see cref="BsonExpression"/>.
        /// </summary>
        /// <typeparam name="T">Document type.</typeparam>
        /// <param name="source">Source queryable.</param>
        /// <param name="fieldExpr">Expression selecting the vector field.</param>
        /// <param name="target">Query vector.</param>
        /// <param name="k">Number of matches to return.</param>
        /// <param name="metric">Optional metric override.</param>
        /// <param name="maxDistance">Optional distance guard.</param>
        /// <returns>A queryable containing the nearest matches.</returns>
        /// <example>
        /// <code><![CDATA[
        /// var matches = collection.Query()
        ///     .TopKNear(BsonExpression.Create("$.Embedding"), queryVector, 5)
        ///     .ToList();
        /// ]]></code>
        /// </example>
        public static ILiteQueryableResult<T> TopKNear<T>(this ILiteQueryable<T> source, BsonExpression fieldExpr, float[] target, int k, VectorDistanceMetric? metric = null, double? maxDistance = null)
        {
            return Unwrap(source).VectorTopKNear(fieldExpr, target, k, ToMetricByte(metric), maxDistance);
        }

        /// <summary>
        /// Orders the queryable by ascending distance to the provided vector while applying deterministic tie-breaking on <c>_id</c>.
        /// </summary>
        /// <typeparam name="T">Document type.</typeparam>
        /// <typeparam name="K">Vector property type.</typeparam>
        /// <param name="source">Source queryable.</param>
        /// <param name="field">Lambda selecting the vector property.</param>
        /// <param name="target">Query vector.</param>
        /// <param name="metric">Optional metric override.</param>
        /// <param name="maxDistance">Optional distance guard.</param>
        /// <returns>An ordered vector queryable.</returns>
        /// <example>
        /// <code><![CDATA[
        /// var ordered = collection.Query()
        ///     .OrderByNearest(x => x.Embedding, queryVector)
        ///     .Limit(3)
        ///     .ToList();
        /// ]]></code>
        /// </example>
        public static ILiteQueryable<T> OrderByNearest<T, K>(this ILiteQueryable<T> source, Expression<Func<T, K>> field, float[] target, VectorDistanceMetric? metric = null, double? maxDistance = null)
        {
            return Unwrap(source).VectorOrderByNearest(field, target, ToMetricByte(metric), maxDistance);
        }

        /// <summary>
        /// Convenience wrapper equivalent to <see cref="OrderByNearest{T, K}(ILiteQueryable{T}, Expression{Func{T, K}}, float[], VectorDistanceMetric?, double?)"/> followed by <see cref="ILiteQueryableResult{T}.Limit(int)"/>.
        /// </summary>
        /// <typeparam name="T">Document type.</typeparam>
        /// <typeparam name="K">Vector property type.</typeparam>
        /// <param name="source">Source queryable.</param>
        /// <param name="field">Lambda selecting the vector property.</param>
        /// <param name="target">Query vector.</param>
        /// <param name="k">Number of matches to limit the results to.</param>
        /// <param name="metric">Optional metric override.</param>
        /// <param name="maxDistance">Optional distance guard.</param>
        /// <returns>A queryable limited to the specified number of nearest matches.</returns>
        /// <example>
        /// <code><![CDATA[
        /// var nearest = collection.Query()
        ///     .Nearest(x => x.Embedding, queryVector, k: 3)
        ///     .ToList();
        /// ]]></code>
        /// </example>
        public static ILiteQueryableResult<T> Nearest<T, K>(this ILiteQueryable<T> source, Expression<Func<T, K>> field, float[] target, int k, VectorDistanceMetric? metric = null, double? maxDistance = null)
        {
            var queryable = Unwrap(source).VectorOrderByNearest(field, target, ToMetricByte(metric), maxDistance);
            return queryable.Limit(k);
        }

        /// <summary>
        /// Projects vector scores (distance or similarity) without recomputing the query.
        /// </summary>
        /// <typeparam name="T">Document type.</typeparam>
        /// <param name="source">Vector-aware query result (e.g., from <c>TopKNear</c>).</param>
        /// <param name="kind">Selects which score to surface.</param>
        /// <returns>A queryable result that yields <see cref="VectorMatch{T}"/> instances.</returns>
        /// <example>
        /// <code><![CDATA[
        /// var scored = collection.Query()
        ///     .TopKNear(x => x.Embedding, queryVector, 5)
        ///     .WithVectorScore(VectorScoreKind.Similarity)
        ///     .ToList();
        /// ]]></code>
        /// </example>
        public static ILiteQueryableResult<VectorMatch<T>> WithVectorScore<T>(this ILiteQueryableResult<T> source, VectorScoreKind kind = VectorScoreKind.Distance)
        {
            return VectorScoreQueryableResult<T>.Create(source, kind);
        }

        private static LiteQueryable<T> Unwrap<T>(ILiteQueryable<T> source)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source is LiteQueryable<T> liteQueryable)
            {
                return liteQueryable;
            }

            throw new ArgumentException("Vector operations require LiteDB's default queryable implementation.", nameof(source));
        }

        private static byte? ToMetricByte(VectorDistanceMetric? metric) => metric.HasValue ? (byte)metric.Value : (byte?)null;
    }
}
