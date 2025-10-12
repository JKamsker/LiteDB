using System;
using System.Reflection;

namespace LiteDB.Plugins
{
    /// <summary>
    /// Defines precedence buckets used when combining binary operators.
    /// </summary>
    public enum BinaryOperatorPrecedence
    {
        Multiplicative = 0,
        Additive = 1,
        Comparison = 2,
        Quantifier = 3,
        LogicalAnd = 4,
        LogicalOr = 5
    }

    /// <summary>
    /// Represents a plugin-supplied binary operator registration.
    /// </summary>
    public sealed class BinaryOperatorRegistration
    {
        public BinaryOperatorRegistration(string token, BsonExpressionType expressionType, BsonBinaryOperator implementation, BinaryOperatorPrecedence precedence = BinaryOperatorPrecedence.Comparison, string source = null)
        {
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentNullException(nameof(token));
            this.Token = token;
            this.ExpressionType = expressionType;
            this.Implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));
            this.Precedence = precedence;
            this.Source = string.IsNullOrWhiteSpace(source) ? $" {token.ToUpperInvariant()} " : source;
        }

        public string Token { get; }

        public BsonExpressionType ExpressionType { get; }

        public BsonBinaryOperator Implementation { get; }

        public BinaryOperatorPrecedence Precedence { get; }

        public string Source { get; }
    }

    /// <summary>
    /// Represents a plugin-supplied function registration.
    /// </summary>
    public sealed class ExpressionFunctionRegistration
    {
        public ExpressionFunctionRegistration(string name, Delegate implementation, BsonExpressionType expressionType, bool convertScalarLeftToEnumerable = true, bool isScalarResult = false)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            this.Name = name;
            this.Implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));
            this.ExpressionType = expressionType;
            this.ConvertScalarLeftToEnumerable = convertScalarLeftToEnumerable;
            this.IsScalarResult = isScalarResult;

            var parameters = implementation.GetMethodInfo().GetParameters();
            if (parameters.Length < 4)
            {
                throw new ArgumentException("Expression functions must accept at least root, collation, parameters, and left arguments.", nameof(implementation));
            }

            this.AdditionalArgumentCount = Math.Max(0, parameters.Length - 5);
        }

        public string Name { get; }

        public Delegate Implementation { get; }

        public BsonExpressionType ExpressionType { get; }

        public bool ConvertScalarLeftToEnumerable { get; }

        public bool IsScalarResult { get; }

        public int AdditionalArgumentCount { get; }
    }
}
