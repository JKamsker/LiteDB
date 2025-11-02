using System;
using System.Linq.Expressions;

namespace LiteDB.Vector
{
    /// <summary>
    /// Extension methods that expose vector-aware index creation for <see cref="ILiteRepository"/>.
    /// </summary>
    public static class LiteRepositoryVectorExtensions
    {
        /// <summary>
        /// Creates a vector index for the repository-managed collection using a <see cref="BsonExpression"/> and explicit name.
        /// </summary>
        /// <param name="repository">Repository instance.</param>
        /// <param name="name">Index name.</param>
        /// <param name="expression">Field expression pointing to the vector data.</param>
        /// <param name="options">Vector index configuration.</param>
        /// <param name="collectionName">Optional collection override.</param>
        /// <returns><c>true</c> when a new index is created; otherwise <c>false</c>.</returns>
        public static bool EnsureIndex<T>(this ILiteRepository repository, string name, BsonExpression expression, VectorIndexOptions options, string? collectionName = null)
        {
            return Unwrap(repository).EnsureVectorIndex<T>(name, expression, VectorExtensionHelpers.CreateOptionsDocument(options), collectionName);
        }

        /// <summary>
        /// Creates a vector index for the repository-managed collection using a <see cref="BsonExpression"/> with an auto-generated name.
        /// </summary>
        /// <param name="repository">Repository instance.</param>
        /// <param name="expression">Field expression pointing to the vector data.</param>
        /// <param name="options">Vector index configuration.</param>
        /// <param name="collectionName">Optional collection override.</param>
        /// <returns><c>true</c> when a new index is created; otherwise <c>false</c>.</returns>
        public static bool EnsureIndex<T>(this ILiteRepository repository, BsonExpression expression, VectorIndexOptions options, string? collectionName = null)
        {
            return Unwrap(repository).EnsureVectorIndex<T>(expression, VectorExtensionHelpers.CreateOptionsDocument(options), collectionName);
        }

        /// <summary>
        /// Creates a vector index for the repository-managed collection using a strongly-typed lambda with an auto-generated name.
        /// </summary>
        /// <param name="repository">Repository instance.</param>
        /// <param name="keySelector">Lambda selecting the vector property.</param>
        /// <param name="options">Vector index configuration.</param>
        /// <param name="collectionName">Optional collection override.</param>
        /// <returns><c>true</c> when a new index is created; otherwise <c>false</c>.</returns>
        public static bool EnsureIndex<T, K>(this ILiteRepository repository, Expression<Func<T, K>> keySelector, VectorIndexOptions options, string? collectionName = null)
        {
            return Unwrap(repository).EnsureVectorIndex(keySelector, VectorExtensionHelpers.CreateOptionsDocument(options), collectionName);
        }

        /// <summary>
        /// Creates a vector index for the repository-managed collection using a strongly-typed lambda with an explicit name.
        /// </summary>
        /// <param name="repository">Repository instance.</param>
        /// <param name="name">Index name.</param>
        /// <param name="keySelector">Lambda selecting the vector property.</param>
        /// <param name="options">Vector index configuration.</param>
        /// <param name="collectionName">Optional collection override.</param>
        /// <returns><c>true</c> when a new index is created; otherwise <c>false</c>.</returns>
        public static bool EnsureIndex<T, K>(this ILiteRepository repository, string name, Expression<Func<T, K>> keySelector, VectorIndexOptions options, string? collectionName = null)
        {
            return Unwrap(repository).EnsureVectorIndex(name, keySelector, VectorExtensionHelpers.CreateOptionsDocument(options), collectionName);
        }

        private static LiteRepository Unwrap(ILiteRepository repository)
        {
            if (repository is null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            if (repository is LiteRepository concrete)
            {
                return concrete;
            }

            throw new ArgumentException("Vector index operations require LiteDB's default repository implementation.", nameof(repository));
        }
    }
}
