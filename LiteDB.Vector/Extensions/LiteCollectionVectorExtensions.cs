using System;
using System.Linq.Expressions;

namespace LiteDB.Vector
{
    /// <summary>
    /// Extension methods that expose vector-aware index creation for <see cref="ILiteCollection{T}"/>.
    /// </summary>
    public static class LiteCollectionVectorExtensions
    {
        /// <summary>
        /// Creates a vector index with an explicit name using a <see cref="BsonExpression"/>.
        /// </summary>
        /// <param name="collection">Collection to index.</param>
        /// <param name="name">Index name.</param>
        /// <param name="expression">Field expression pointing to the vector data.</param>
        /// <param name="options">Vector index configuration.</param>
        /// <returns><c>true</c> when a new index is created; otherwise <c>false</c>.</returns>
        public static bool EnsureIndex<T>(this ILiteCollection<T> collection, string name, BsonExpression expression, VectorIndexOptions options)
        {
            return Unwrap(collection).EnsureVectorIndex(name, expression, VectorExtensionHelpers.CreateOptionsDocument(options));
        }

        /// <summary>
        /// Creates a vector index using a <see cref="BsonExpression"/> with an auto-generated name.
        /// </summary>
        /// <param name="collection">Collection to index.</param>
        /// <param name="expression">Field expression pointing to the vector data.</param>
        /// <param name="options">Vector index configuration.</param>
        /// <returns><c>true</c> when a new index is created; otherwise <c>false</c>.</returns>
        public static bool EnsureIndex<T>(this ILiteCollection<T> collection, BsonExpression expression, VectorIndexOptions options)
        {
            return Unwrap(collection).EnsureVectorIndex(expression, VectorExtensionHelpers.CreateOptionsDocument(options));
        }

        /// <summary>
        /// Creates a vector index using a strongly-typed lambda expression with an auto-generated name.
        /// </summary>
        /// <param name="collection">Collection to index.</param>
        /// <param name="keySelector">Lambda selecting the vector property.</param>
        /// <param name="options">Vector index configuration.</param>
        /// <returns><c>true</c> when a new index is created; otherwise <c>false</c>.</returns>
        public static bool EnsureIndex<T, K>(this ILiteCollection<T> collection, Expression<Func<T, K>> keySelector, VectorIndexOptions options)
        {
            return Unwrap(collection).EnsureVectorIndex(keySelector, VectorExtensionHelpers.CreateOptionsDocument(options));
        }

        /// <summary>
        /// Creates a vector index using a strongly-typed lambda expression and an explicit name.
        /// </summary>
        /// <param name="collection">Collection to index.</param>
        /// <param name="name">Index name.</param>
        /// <param name="keySelector">Lambda selecting the vector property.</param>
        /// <param name="options">Vector index configuration.</param>
        /// <returns><c>true</c> when a new index is created; otherwise <c>false</c>.</returns>
        public static bool EnsureIndex<T, K>(this ILiteCollection<T> collection, string name, Expression<Func<T, K>> keySelector, VectorIndexOptions options)
        {
            return Unwrap(collection).EnsureVectorIndex(name, keySelector, VectorExtensionHelpers.CreateOptionsDocument(options));
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
    }
}
