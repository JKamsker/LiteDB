using LiteDB.Engine;
using LiteDB.Plugins;

using System;
using System.Collections.Generic;
namespace LiteDB
{
    /// <summary>
    /// Class is a result from optimized QueryBuild. Indicate how engine must run query - there is no more decisions to engine made, must only execute as query was defined
    /// </summary>
    public partial class Query
    {
        private static IExpressionRegistry DefaultExpressions => PluginContextFallbacks.Expressions;

        /// <summary>
        /// Indicate when a query must execute in ascending order
        /// </summary>
        public const int Ascending = 1;

        /// <summary>
        /// Indicate when a query must execute in descending order
        /// </summary>
        public const int Descending = -1;

        /// <summary>
        /// Returns all documents
        /// </summary>
        public static Query All()
        {
            return new Query();
        }

        /// <summary>
        /// Returns all documents ordered by the primary key using the provided registry.
        /// </summary>
        public static Query All(int order, IExpressionRegistry registry)
        {
            var effectiveRegistry = RequireRegistry(registry);

            var query = new Query();
            query.OrderBy.Add(new QueryOrder(BsonExpression.Create("_id", effectiveRegistry), order));
            return query;
        }

        /// <summary>
        /// Returns all documents
        /// </summary>
        public static Query All(IExpressionRegistry registry)
        {
            RequireRegistry(registry);
            return new Query();
        }

        /// <summary>
        /// Returns all documents ordered by the specified field using the provided registry.
        /// </summary>
        public static Query All(string field, int order, IExpressionRegistry registry)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            var effectiveRegistry = RequireRegistry(registry);

            var query = new Query();
            query.OrderBy.Add(new QueryOrder(BsonExpression.Create(field, effectiveRegistry), order));
            return query;
        }

        /// <summary>
        /// Returns all documents ordered by the specified field using the provided registry.
        /// </summary>
        public static Query All(string field, IExpressionRegistry registry)
        {
            return All(field, Ascending, registry);
        }

        /// <summary>
        /// Returns all documents that value are equals to value (=)
        /// </summary>
        public static BsonExpression EQ(string field, BsonValue value, IExpressionRegistry registry)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            var effectiveRegistry = RequireRegistry(registry);

            return BsonExpression.Create($"{field} = {value ?? BsonValue.Null}", effectiveRegistry);
        }

        /// <summary>
        /// Returns all documents that value are less than value (&lt;)
        /// </summary>
        public static BsonExpression LT(string field, BsonValue value, IExpressionRegistry registry)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            var effectiveRegistry = RequireRegistry(registry);

            return BsonExpression.Create($"{field} < {value ?? BsonValue.Null}", effectiveRegistry);
        }

        /// <summary>
        /// Returns all documents that value are less than or equals value (&lt;=)
        /// </summary>
        public static BsonExpression LTE(string field, BsonValue value, IExpressionRegistry registry)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            var effectiveRegistry = RequireRegistry(registry);

            return BsonExpression.Create($"{field} <= {value ?? BsonValue.Null}", effectiveRegistry);
        }

        /// <summary>
        /// Returns all document that value are greater than value (&gt;)
        /// </summary>
        public static BsonExpression GT(string field, BsonValue value, IExpressionRegistry registry)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            var effectiveRegistry = RequireRegistry(registry);

            return BsonExpression.Create($"{field} > {value ?? BsonValue.Null}", effectiveRegistry);
        }

        /// <summary>
        /// Returns all documents that value are greater than or equals value (&gt;=)
        /// </summary>
        public static BsonExpression GTE(string field, BsonValue value, IExpressionRegistry registry)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            var effectiveRegistry = RequireRegistry(registry);

            return BsonExpression.Create($"{field} >= {value ?? BsonValue.Null}", effectiveRegistry);
        }

        /// <summary>
        /// Returns all document that values are between "start" and "end" values (BETWEEN)
        /// </summary>
        public static BsonExpression Between(string field, BsonValue start, BsonValue end, IExpressionRegistry registry)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            var effectiveRegistry = RequireRegistry(registry);

            return BsonExpression.Create($"{field} BETWEEN {start ?? BsonValue.Null} AND {end ?? BsonValue.Null}", effectiveRegistry);
        }

        /// <summary>
        /// Returns all documents that starts with value (LIKE)
        /// </summary>
        public static BsonExpression StartsWith(string field, string value, IExpressionRegistry registry)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));
            if (value.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(value));

            var effectiveRegistry = RequireRegistry(registry);

            return BsonExpression.Create($"{field} LIKE {(new BsonValue(value + "%"))}", effectiveRegistry);
        }

        /// <summary>
        /// Returns all documents that contains value (CONTAINS) - string Contains
        /// </summary>
        public static BsonExpression Contains(string field, string value, IExpressionRegistry registry)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));
            if (value.IsNullOrEmpty()) throw new ArgumentNullException(nameof(value));

            var effectiveRegistry = RequireRegistry(registry);

            return BsonExpression.Create($"{field} LIKE {(new BsonValue("%" + value + "%"))}", effectiveRegistry);
        }

        /// <summary>
        /// Returns all documents that are not equals to value (not equals)
        /// </summary>
        public static BsonExpression Not(string field, BsonValue value, IExpressionRegistry registry)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            var effectiveRegistry = RequireRegistry(registry);

            return BsonExpression.Create($"{field} != {value ?? BsonValue.Null}", effectiveRegistry);
        }

        /// <summary>
        /// Returns all documents that has value in values list (IN)
        /// </summary>
        public static BsonExpression In(string field, BsonArray value, IExpressionRegistry registry)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));
            if (value == null) throw new ArgumentNullException(nameof(value));

            var effectiveRegistry = RequireRegistry(registry);

            return BsonExpression.Create($"{field} IN {value}", effectiveRegistry);
        }

        /// <summary>
        /// Returns all documents that has value in values list (IN)
        /// </summary>
        public static BsonExpression In(string field, IExpressionRegistry registry, params BsonValue[] values)
        {
            return In(field, new BsonArray(values ?? Array.Empty<BsonValue>()), registry);
        }

        /// <summary>
        /// Returns all documents that has value in values list (IN)
        /// </summary>
        public static BsonExpression In(string field, IEnumerable<BsonValue> values, IExpressionRegistry registry)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));

            return In(field, new BsonArray(values), registry);
        }

        /// <summary>
        /// Get all operands to works with array or enumerable values
        /// </summary>
        public static QueryAny Any(IExpressionRegistry registry) => new QueryAny(RequireRegistry(registry));

        /// <summary>
        /// Get all operands to works with array or enumerable values
        /// </summary>
        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static QueryAny Any() => new QueryAny();

        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static Query All(int order = Ascending)
        {
            return All(order, DefaultExpressions);
        }

        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static Query All(string field, int order = Ascending)
        {
            return All(field, order, DefaultExpressions);
        }

        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static BsonExpression EQ(string field, BsonValue value)
        {
            return EQ(field, value, DefaultExpressions);
        }

        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static BsonExpression LT(string field, BsonValue value)
        {
            return LT(field, value, DefaultExpressions);
        }

        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static BsonExpression LTE(string field, BsonValue value)
        {
            return LTE(field, value, DefaultExpressions);
        }

        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static BsonExpression GT(string field, BsonValue value)
        {
            return GT(field, value, DefaultExpressions);
        }

        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static BsonExpression GTE(string field, BsonValue value)
        {
            return GTE(field, value, DefaultExpressions);
        }

        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static BsonExpression Between(string field, BsonValue start, BsonValue end)
        {
            return Between(field, start, end, DefaultExpressions);
        }

        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static BsonExpression StartsWith(string field, string value)
        {
            return StartsWith(field, value, DefaultExpressions);
        }

        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static BsonExpression Contains(string field, string value)
        {
            return Contains(field, value, DefaultExpressions);
        }

        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static BsonExpression Not(string field, BsonValue value)
        {
            return Not(field, value, DefaultExpressions);
        }

        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static BsonExpression In(string field, BsonArray value)
        {
            return In(field, value, DefaultExpressions);
        }

        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static BsonExpression In(string field, params BsonValue[] values)
        {
            return In(field, DefaultExpressions, values ?? Array.Empty<BsonValue>());
        }

        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static BsonExpression In(string field, IEnumerable<BsonValue> values)
        {
            return In(field, values, DefaultExpressions);
        }

        /// <summary>
        /// Returns document that exists in BOTH queries results. If both queries has indexes, left query has index preference (other side will be run in full scan)
        /// </summary>
        public static BsonExpression And(BsonExpression left, BsonExpression right, IExpressionRegistry registry)
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));

            var effectiveRegistry = RequireRegistry(registry);

            return BsonExpression.Create($"({left.Source} AND {right.Source})", effectiveRegistry);
        }

        /// <summary>
        /// Returns document that exists in BOTH queries results. If both queries has indexes, left query has index preference (other side will be run in full scan)
        /// </summary>
        public static BsonExpression And(BsonExpression left, BsonExpression right)
        {
            var registry = ResolveRegistry(left, right);

            return And(left, right, registry);
        }

        /// <summary>
        /// Returns document that exists in ALL queries results.
        /// </summary>
        public static BsonExpression And(params BsonExpression[] queries)
        {
            if (queries == null || queries.Length < 2) throw new ArgumentException("At least two Query should be passed");

            var registry = ResolveRegistry(queries);

            return And(registry, queries);
        }

        /// <summary>
        /// Returns document that exists in ALL queries results using an explicit registry.
        /// </summary>
        public static BsonExpression And(IExpressionRegistry registry, params BsonExpression[] queries)
        {
            if (queries == null || queries.Length < 2) throw new ArgumentException("At least two Query should be passed");

            var effectiveRegistry = RequireRegistry(registry);

            var left = queries[0];

            for (int i = 1; i < queries.Length; i++)
            {
                left = And(left, queries[i], effectiveRegistry);
            }

            return left;
        }

        /// <summary>
        /// Returns documents that exists in ANY queries results (Union).
        /// </summary>
        public static BsonExpression Or(BsonExpression left, BsonExpression right, IExpressionRegistry registry)
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));

            var effectiveRegistry = RequireRegistry(registry);

            return BsonExpression.Create($"({left.Source} OR {right.Source})", effectiveRegistry);
        }

        /// <summary>
        /// Returns documents that exists in ANY queries results (Union).
        /// </summary>
        public static BsonExpression Or(BsonExpression left, BsonExpression right)
        {
            var registry = ResolveRegistry(left, right);

            return Or(left, right, registry);
        }

        /// <summary>
        /// Returns document that exists in ANY queries results (Union).
        /// </summary>
        public static BsonExpression Or(params BsonExpression[] queries)
        {
            if (queries == null || queries.Length < 2) throw new ArgumentException("At least two Query should be passed");

            var registry = ResolveRegistry(queries);

            return Or(registry, queries);
        }

        /// <summary>
        /// Returns document that exists in ANY queries results (Union) using an explicit registry.
        /// </summary>
        public static BsonExpression Or(IExpressionRegistry registry, params BsonExpression[] queries)
        {
            if (queries == null || queries.Length < 2) throw new ArgumentException("At least two Query should be passed");

            var effectiveRegistry = RequireRegistry(registry);

            var left = queries[0];

            for (int i = 1; i < queries.Length; i++)
            {
                left = Or(left, queries[i], effectiveRegistry);
            }

            return left;
        }

        private static IExpressionRegistry ResolveRegistry(params BsonExpression[] expressions)
        {
            if (expressions == null)
            {
                return DefaultExpressions;
            }

            foreach (var expression in expressions)
            {
                if (expression?.Registry != null)
                {
                    return expression.Registry;
                }
            }

            return DefaultExpressions;
        }

        private static IExpressionRegistry RequireRegistry(IExpressionRegistry registry)
        {
            return registry ?? throw new ArgumentNullException(nameof(registry));
        }
    }
}
