using System;
using System.Collections.Generic;

namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Registry for plugin-defined query operators.
    /// Example: LiteDB.Vector registers VECTOR_KNN operator for k-nearest-neighbor queries.
    /// </summary>
    public interface IQueryOperatorRegistry
    {
        /// <summary>
        /// Registers a query operator provided by a plugin.
        /// </summary>
        /// <param name="registration">The operator registration.</param>
        /// <exception cref="ArgumentNullException">When registration is null.</exception>
        /// <exception cref="InvalidOperationException">When an operator with the same name is already registered.</exception>
        void Register(QueryOperatorRegistration registration);

        /// <summary>
        /// Attempts to retrieve an operator registration by name.
        /// </summary>
        /// <param name="operatorName">The operator name (case-insensitive).</param>
        /// <param name="registration">Receives the registration if found.</param>
        /// <returns>True if the operator was found, false otherwise.</returns>
        bool TryGet(string operatorName, out QueryOperatorRegistration registration);

        /// <summary>
        /// Gets all registered query operators.
        /// </summary>
        IReadOnlyCollection<QueryOperatorRegistration> GetAll();
    }

    /// <summary>
    /// Describes a plugin-registered query operator.
    /// </summary>
    public sealed class QueryOperatorRegistration
    {
        /// <summary>
        /// Initializes a new query operator registration.
        /// </summary>
        /// <param name="pluginId">The identifier of the owning plugin.</param>
        /// <param name="operatorName">The operator name (e.g., "VECTOR_KNN").</param>
        /// <param name="expressionType">The BSON expression type for this operator.</param>
        /// <param name="parser">The parser delegate that constructs the expression.</param>
        public QueryOperatorRegistration(
            string pluginId,
            string operatorName,
            BsonExpressionType expressionType,
            Func<BsonExpression[], BsonExpression> parser)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
                throw new ArgumentException("Plugin ID cannot be null or whitespace.", nameof(pluginId));

            if (string.IsNullOrWhiteSpace(operatorName))
                throw new ArgumentException("Operator name cannot be null or whitespace.", nameof(operatorName));

            PluginId = pluginId;
            OperatorName = operatorName;
            ExpressionType = expressionType;
            Parser = parser ?? throw new ArgumentNullException(nameof(parser));
        }

        /// <summary>
        /// Gets the identifier of the plugin that registered this operator.
        /// </summary>
        public string PluginId { get; }

        /// <summary>
        /// Gets the operator name.
        /// </summary>
        public string OperatorName { get; }

        /// <summary>
        /// Gets the BSON expression type for this operator.
        /// </summary>
        public BsonExpressionType ExpressionType { get; }

        /// <summary>
        /// Gets the parser delegate that constructs expressions from this operator.
        /// </summary>
        public Func<BsonExpression[], BsonExpression> Parser { get; }
    }
}
