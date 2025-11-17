using System;
using System.Collections.Generic;
using LiteDB;
using LiteDB.Plugins;

namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Registry contract for plugin-defined query operators.
    /// </summary>
    public interface IQueryOperatorRegistry
    {
        void Register(QueryOperatorRegistration registration);

        bool TryGet(string operatorName, out QueryOperatorRegistration registration);

        IReadOnlyCollection<QueryOperatorRegistration> Registered { get; }
    }

    /// <summary>
    /// Describes a custom query operator contributed by a plugin.
    /// </summary>
    public sealed class QueryOperatorRegistration
    {
        public QueryOperatorRegistration(
            string pluginId,
            string operatorName,
            BsonExpressionType expressionType,
            Func<BsonExpression[], BsonExpression> parser,
            BinaryOperatorPrecedence precedence = BinaryOperatorPrecedence.Comparison)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException("Plugin identifier must be provided.", nameof(pluginId));
            }

            if (string.IsNullOrWhiteSpace(operatorName))
            {
                throw new ArgumentException("Operator name must be provided.", nameof(operatorName));
            }

            PluginId = pluginId;
            OperatorName = operatorName;
            ExpressionType = expressionType;
            Parser = parser ?? throw new ArgumentNullException(nameof(parser));
            Precedence = precedence;
        }

        public string PluginId { get; }

        public string OperatorName { get; }

        public BsonExpressionType ExpressionType { get; }

        public Func<BsonExpression[], BsonExpression> Parser { get; }

        public BinaryOperatorPrecedence Precedence { get; }
    }
}
