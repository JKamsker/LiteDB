using System;
using System.Linq.Expressions;

namespace LiteDB.Vector.Extensions
{
    /// <summary>
    /// Extension methods for ILiteCollection that provide vector index functionality.
    /// These methods are only available when the LiteDB.Vector plugin is referenced.
    /// </summary>
    public static class LiteCollectionVectorExtensions
    {
        /// <summary>
        /// Creates a vector index on a specified field using a BSON expression.
        /// </summary>
        /// <typeparam name="T">The document type.</typeparam>
        /// <param name="collection">The collection to index.</param>
        /// <param name="name">The name of the index.</param>
        /// <param name="expression">The BSON expression identifying the field to index.</param>
        /// <param name="options">Vector index options specifying dimensions and metric.</param>
        /// <returns>True if the index was created; false if it already exists.</returns>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        /// <exception cref="LiteException">Thrown when the vector plugin is not registered (LITE2002).</exception>
        public static bool EnsureIndex<T>(
            this ILiteCollection<T> collection,
            string name,
            BsonExpression expression,
            VectorIndexOptions options)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            if (options == null) throw new ArgumentNullException(nameof(options));

            // TODO: Implement using plugin registry
            // For now, this will need to access the internal engine through reflection or
            // use a proper plugin-based API once the core is updated

            throw Utils.VectorCompatibility.FeatureNotAvailable("EnsureIndex with VectorIndexOptions");
        }

        /// <summary>
        /// Creates a vector index on a specified field using a BSON expression.
        /// The index name will be automatically generated based on the expression.
        /// </summary>
        /// <typeparam name="T">The document type.</typeparam>
        /// <param name="collection">The collection to index.</param>
        /// <param name="expression">The BSON expression identifying the field to index.</param>
        /// <param name="options">Vector index options specifying dimensions and metric.</param>
        /// <returns>True if the index was created; false if it already exists.</returns>
        public static bool EnsureIndex<T>(
            this ILiteCollection<T> collection,
            BsonExpression expression,
            VectorIndexOptions options)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (expression == null) throw new ArgumentNullException(nameof(expression));

            // Auto-generate index name from expression
            var indexName = "$" + expression.Source;
            return EnsureIndex(collection, indexName, expression, options);
        }

        /// <summary>
        /// Creates a vector index on a specified field using a lambda expression.
        /// </summary>
        /// <typeparam name="T">The document type.</typeparam>
        /// <typeparam name="K">The field type.</typeparam>
        /// <param name="collection">The collection to index.</param>
        /// <param name="keySelector">Lambda expression selecting the field to index.</param>
        /// <param name="options">Vector index options specifying dimensions and metric.</param>
        /// <returns>True if the index was created; false if it already exists.</returns>
        public static bool EnsureIndex<T, K>(
            this ILiteCollection<T> collection,
            Expression<Func<T, K>> keySelector,
            VectorIndexOptions options)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

            var expression = BsonExpression.Create(keySelector);
            return EnsureIndex(collection, expression, options);
        }

        /// <summary>
        /// Creates a vector index on a specified field using a lambda expression.
        /// </summary>
        /// <typeparam name="T">The document type.</typeparam>
        /// <typeparam name="K">The field type.</typeparam>
        /// <param name="collection">The collection to index.</param>
        /// <param name="name">The name of the index.</param>
        /// <param name="keySelector">Lambda expression selecting the field to index.</param>
        /// <param name="options">Vector index options specifying dimensions and metric.</param>
        /// <returns>True if the index was created; false if it already exists.</returns>
        public static bool EnsureIndex<T, K>(
            this ILiteCollection<T> collection,
            string name,
            Expression<Func<T, K>> keySelector,
            VectorIndexOptions options)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

            var expression = BsonExpression.Create(keySelector);
            return EnsureIndex(collection, name, expression, options);
        }
    }
}
