using System;
using System.Linq.Expressions;

namespace LiteDB.Vector
{
    /// <summary>
    /// Extension methods that expose vector-aware index creation for <see cref="ILiteRepository"/>.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// var options = new VectorIndexOptions(384);
    /// repository.EnsureIndex<Article>(x => x.Embedding, options);
    /// ]]></code>
    /// </example>
    public static class LiteRepositoryVectorExtensions
    {
        /// <summary>
        /// Creates a vector index for the repository-managed collection using a <see cref="BsonExpression"/> and explicit name.
        /// </summary>
        /// <typeparam name="T">Document type managed by the repository.</typeparam>
        /// <param name="repository">Repository instance.</param>
        /// <param name="name">Index name.</param>
        /// <param name="expression">Field expression pointing to the vector data.</param>
        /// <param name="options">Vector index configuration.</param>
        /// <param name="collectionName">Optional collection override.</param>
        /// <returns><c>true</c> when a new index is created; otherwise <c>false</c>.</returns>
        /// <example>
        /// <code><![CDATA[
        /// repository.EnsureIndex<Article>("embedding_idx", BsonExpression.Create("$.Embedding"), new VectorIndexOptions(384));
        /// ]]></code>
        /// </example>
        public static bool EnsureIndex<T>(this ILiteRepository repository, string name, BsonExpression expression, VectorIndexOptions options, string? collectionName = null)
        {
            return Unwrap(repository).EnsureVectorIndex<T>(name, expression, VectorExtensionHelpers.CreateOptionsDocument(options), collectionName);
        }

        /// <summary>
        /// Creates a vector index for the repository-managed collection using a <see cref="BsonExpression"/> with an auto-generated name.
        /// </summary>
        /// <typeparam name="T">Document type managed by the repository.</typeparam>
        /// <param name="repository">Repository instance.</param>
        /// <param name="expression">Field expression pointing to the vector data.</param>
        /// <param name="options">Vector index configuration.</param>
        /// <param name="collectionName">Optional collection override.</param>
        /// <returns><c>true</c> when a new index is created; otherwise <c>false</c>.</returns>
        /// <example>
        /// <code><![CDATA[
        /// repository.EnsureIndex<Article>(BsonExpression.Create("$.Embedding"), new VectorIndexOptions(384));
        /// ]]></code>
        /// </example>
        public static bool EnsureIndex<T>(this ILiteRepository repository, BsonExpression expression, VectorIndexOptions options, string? collectionName = null)
        {
            return Unwrap(repository).EnsureVectorIndex<T>(expression, VectorExtensionHelpers.CreateOptionsDocument(options), collectionName);
        }

        /// <summary>
        /// Creates a vector index for the repository-managed collection using a strongly-typed lambda with an auto-generated name.
        /// </summary>
        /// <typeparam name="T">Document type managed by the repository.</typeparam>
        /// <typeparam name="K">Property type returned by the selector.</typeparam>
        /// <param name="repository">Repository instance.</param>
        /// <param name="keySelector">Lambda selecting the vector property.</param>
        /// <param name="options">Vector index configuration.</param>
        /// <param name="collectionName">Optional collection override.</param>
        /// <returns><c>true</c> when a new index is created; otherwise <c>false</c>.</returns>
        /// <example>
        /// <code><![CDATA[
        /// repository.EnsureIndex<Article, float[]>(x => x.Embedding, new VectorIndexOptions(384));
        /// ]]></code>
        /// </example>
        public static bool EnsureIndex<T, K>(this ILiteRepository repository, Expression<Func<T, K>> keySelector, VectorIndexOptions options, string? collectionName = null)
        {
            return Unwrap(repository).EnsureVectorIndex(keySelector, VectorExtensionHelpers.CreateOptionsDocument(options), collectionName);
        }

        /// <summary>
        /// Creates a vector index for the repository-managed collection using a strongly-typed lambda with an explicit name.
        /// </summary>
        /// <typeparam name="T">Document type managed by the repository.</typeparam>
        /// <typeparam name="K">Property type returned by the selector.</typeparam>
        /// <param name="repository">Repository instance.</param>
        /// <param name="name">Index name.</param>
        /// <param name="keySelector">Lambda selecting the vector property.</param>
        /// <param name="options">Vector index configuration.</param>
        /// <param name="collectionName">Optional collection override.</param>
        /// <returns><c>true</c> when a new index is created; otherwise <c>false</c>.</returns>
        /// <example>
        /// <code><![CDATA[
        /// repository.EnsureIndex<Article, float[]>("embedding_idx", x => x.Embedding, new VectorIndexOptions(384));
        /// ]]></code>
        /// </example>
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
