using LiteDB.Engine;
using LiteDB.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using static LiteDB.Constants;

namespace LiteDB
{
    public class QueryAny
    {
        private readonly IExpressionRegistry _registry;

        /// <summary>
        /// Initializes a new instance of the <see cref="QueryAny"/> class using the provided expression registry.
        /// </summary>
        /// <param name="registry">The registry used to build the generated expressions.</param>
        public QueryAny(IExpressionRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="QueryAny"/> class using the default expression registry.
        /// </summary>
        [Obsolete("Use the constructor that accepts IExpressionRegistry explicitly.")]
        public QueryAny()
            : this(PluginContextFallbacks.Expressions)
        {
        }

        private BsonExpression CreateExpression(string expression)
        {
            return BsonExpression.Create(expression, _registry);
        }

        /// <summary>
        /// Returns all documents for which at least one value in arrayFields is equal to value
        /// </summary>
        public BsonExpression EQ(string arrayField, BsonValue value)
        {
            if (arrayField.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(arrayField));

            return this.CreateExpression($"{arrayField} ANY = {value ?? BsonValue.Null}");
        }

        /// <summary>
        /// Returns all documents for which at least one value in arrayFields are less tha to value (&lt;)
        /// </summary>
        public BsonExpression LT(string arrayField, BsonValue value)
        {
            if (arrayField.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(arrayField));

            return this.CreateExpression($"{arrayField} ANY < {value ?? BsonValue.Null}");
        }

        /// <summary>
        /// Returns all documents for which at least one value in arrayFields are less than or equals value (&lt;=)
        /// </summary>
        public BsonExpression LTE(string arrayField, BsonValue value)
        {
            if (arrayField.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(arrayField));

            return this.CreateExpression($"{arrayField} ANY <= {value ?? BsonValue.Null}");
        }

        /// <summary>
        /// Returns all documents for which at least one value in arrayFields are greater than value (&gt;)
        /// </summary>
        public BsonExpression GT(string arrayField, BsonValue value)
        {
            if (arrayField.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(arrayField));

            return this.CreateExpression($"{arrayField} ANY > {value ?? BsonValue.Null}");

        }

        /// <summary>
        /// Returns all documents for which at least one value in arrayFields are greater than or equals value (&gt;=)
        /// </summary>
        public BsonExpression GTE(string arrayField, BsonValue value)
        {
            if (arrayField.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(arrayField));

            return this.CreateExpression($"{arrayField} ANY >= {value ?? BsonValue.Null}");
        }

        /// <summary>
        /// Returns all documents for which at least one value in arrayFields are between "start" and "end" values (BETWEEN)
        /// </summary>
        public BsonExpression Between(string arrayField, BsonValue start, BsonValue end)
        {
            if (arrayField.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(arrayField));

            return this.CreateExpression($"{arrayField} ANY BETWEEN {start ?? BsonValue.Null} AND {end ?? BsonValue.Null}");
        }

        /// <summary>
        /// Returns all documents for which at least one value in arrayFields starts with value (LIKE)
        /// </summary>
        public BsonExpression StartsWith(string arrayField, string value)
        {
            if (arrayField.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(arrayField));
            if (value.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(value));

            return this.CreateExpression($"{arrayField} ANY LIKE {(new BsonValue(value + "%"))}");
        }

        /// <summary>
        /// Returns all documents for which at least one value in arrayFields are not equals to value (not equals)
        /// </summary>
        public BsonExpression Not(string arrayField, BsonValue value)
        {
            if (arrayField.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(arrayField));

            return this.CreateExpression($"{arrayField} ANY != {value ?? BsonValue.Null}");
        }
    }
}
