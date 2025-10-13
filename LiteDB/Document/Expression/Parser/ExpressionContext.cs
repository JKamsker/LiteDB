using LiteDB.Engine;
using LiteDB.Plugins;
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
        private static readonly ParameterExpression _sourceParameter = Expression.Parameter(typeof(IEnumerable<BsonDocument>), "source");
        private static readonly ParameterExpression _rootParameter = Expression.Parameter(typeof(BsonDocument), "root");
        private static readonly ParameterExpression _currentParameter = Expression.Parameter(typeof(BsonValue), "current");
        private static readonly ParameterExpression _collationParameter = Expression.Parameter(typeof(Collation), "collation");
        private static readonly ParameterExpression _parametersParameter = Expression.Parameter(typeof(BsonDocument), "parameters");

        public ExpressionContext(IExpressionRegistry registry)
        {
            this.Source = _sourceParameter;
            this.Root = _rootParameter;
            this.Current = _currentParameter;
            this.Collation = _collationParameter;
            this.Parameters = _parametersParameter;
            this.ExpressionRegistry = registry;
        }

        public ParameterExpression Source { get; }
        public ParameterExpression Root { get; }
        public ParameterExpression Current { get; }
        public ParameterExpression Collation { get; }
        public ParameterExpression Parameters { get; }

        public IExpressionRegistry ExpressionRegistry { get; }
    }
}
