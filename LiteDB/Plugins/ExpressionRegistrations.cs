using System;
using System.Collections.Generic;
using System.Reflection;

namespace LiteDB.Plugins
{
    /// <summary>
    /// Represents the metadata required to add a binary operator to the expression parser.
    /// </summary>
    public sealed class BinaryOperatorRegistration
    {
        public BinaryOperatorRegistration(string token, string source, BsonExpressionType expressionType, BsonBinaryOperator implementation, int precedence)
        {
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentNullException(nameof(token));
            if (string.IsNullOrEmpty(source)) throw new ArgumentNullException(nameof(source));
            if (implementation == null) throw new ArgumentNullException(nameof(implementation));

            this.Token = token.ToUpperInvariant();
            this.Source = source;
            this.ExpressionType = expressionType;
            this.Implementation = implementation;
            this.Precedence = precedence;
        }

        public string Token { get; }

        public string Source { get; }

        public BsonExpressionType ExpressionType { get; }

        public BsonBinaryOperator Implementation { get; }

        public int Precedence { get; }
    }

    /// <summary>
    /// Snapshot of expression configuration registered by plugins.
    /// </summary>
    public sealed class ExpressionParserConfiguration
    {
        public static ExpressionParserConfiguration Empty { get; } = new ExpressionParserConfiguration(Array.Empty<BinaryOperatorRegistration>(), new Dictionary<string, MethodInfo>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        internal ExpressionParserConfiguration(IReadOnlyList<BinaryOperatorRegistration> operators, IReadOnlyDictionary<string, MethodInfo> functions, IReadOnlyCollection<string> keywords)
        {
            this.Operators = operators ?? throw new ArgumentNullException(nameof(operators));
            this.Functions = functions ?? throw new ArgumentNullException(nameof(functions));
            this.Keywords = keywords ?? throw new ArgumentNullException(nameof(keywords));
        }

        internal IReadOnlyList<BinaryOperatorRegistration> Operators { get; }

        internal IReadOnlyDictionary<string, MethodInfo> Functions { get; }

        internal IReadOnlyCollection<string> Keywords { get; }

        internal bool IsEmpty => this.Operators.Count == 0 && this.Functions.Count == 0 && this.Keywords.Count == 0;
    }

    internal static class ExpressionRegistryHelpers
    {
        public static string CreateFunctionKey(string name, int parameterCount)
        {
            if (parameterCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(parameterCount));
            }

            return name.ToUpperInvariant() + "~" + parameterCount;
        }
    }
}
