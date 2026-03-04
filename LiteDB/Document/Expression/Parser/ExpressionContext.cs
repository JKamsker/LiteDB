using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Plugins.Query;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using static LiteDB.Constants;

namespace LiteDB
{
    internal class ExpressionContext
    {
        public ExpressionContext(IExpressionRegistry registry, IQueryOperatorRegistry queryOperators)
        {
            this.Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.QueryOperators = queryOperators ?? throw new ArgumentNullException(nameof(queryOperators));
            this.Source = Expression.Parameter(typeof(IEnumerable<BsonDocument>), "source");
            this.Root = Expression.Parameter(typeof(BsonDocument), "root");
            this.Current = Expression.Parameter(typeof(BsonValue), "current");
            this.Collation = Expression.Parameter(typeof(Collation), "collation");
            this.Parameters = Expression.Parameter(typeof(BsonDocument), "parameters");
        }

        public IExpressionRegistry Registry { get; }
        public IQueryOperatorRegistry QueryOperators { get; }
        public ParameterExpression Source { get; }
        public ParameterExpression Root { get; }
        public ParameterExpression Current { get; }
        public ParameterExpression Collation { get; }
        public ParameterExpression Parameters { get; }
    }
}
