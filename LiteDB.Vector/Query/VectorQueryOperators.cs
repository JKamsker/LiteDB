using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using LiteDB;
using LiteDB.Plugins;
using LiteDB.Plugins.Query;

namespace LiteDB.Vector.Query
{
    /// <summary>
    /// Provides helpers for registering plugin-owned query operators.
    /// </summary>
    internal static class VectorQueryOperators
    {
        private static readonly MethodInfo EvaluateVectorKnnMethod =
            typeof(VectorQueryOperators).GetMethod(nameof(EvaluateVectorKnn), BindingFlags.NonPublic | BindingFlags.Static);

        public static QueryOperatorRegistration CreateVectorKnn(string pluginId)
        {
            if (pluginId == null)
            {
                throw new ArgumentNullException(nameof(pluginId));
            }

            return new QueryOperatorRegistration(
                pluginId,
                "VECTOR_KNN",
                BsonExpressionType.Call,
                ParseVectorKnn,
                BinaryOperatorPrecedence.Comparison);
        }

        private static BsonExpression ParseVectorKnn(BsonExpression[] operands)
        {
            if (operands == null)
            {
                throw new ArgumentNullException(nameof(operands));
            }

            if (operands.Length != 2)
            {
                throw new LiteException(0, "VECTOR_KNN requires two operands: a vector field/path and a target expression.");
            }

            var left = operands[0] ?? throw new LiteException(0, "VECTOR_KNN is missing the vector field operand.");
            var right = operands[1] ?? throw new LiteException(0, "VECTOR_KNN is missing the target operand.");

            if (EvaluateVectorKnnMethod == null)
            {
                throw new InvalidOperationException("VECTOR_KNN evaluator is unavailable.");
            }

            var call = Expression.Call(EvaluateVectorKnnMethod, left.Expression, right.Expression);

            return new BsonExpression
            {
                Type = BsonExpressionType.Call,
                Source = $"{left.Source} VECTOR_KNN {right.Source}",
                IsImmutable = left.IsImmutable && right.IsImmutable,
                UseSource = left.UseSource || right.UseSource,
                IsScalar = true,
                Fields = MergeFields(left.Fields, right.Fields),
                Parameters = MergeParameters(left.Parameters, right.Parameters),
                Expression = call,
                Left = left,
                Right = right,
                CustomExpressionName = "VECTOR_KNN"
            };
        }

        private static HashSet<string> MergeFields(HashSet<string> left, HashSet<string> right)
        {
            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (left != null)
            {
                foreach (var field in left)
                {
                    fields.Add(field);
                }
            }

            if (right != null)
            {
                foreach (var field in right)
                {
                    fields.Add(field);
                }
            }

            return fields;
        }

        private static BsonDocument MergeParameters(BsonDocument left, BsonDocument right)
        {
            var parameters = new BsonDocument();

            if (left != null)
            {
                foreach (var kvp in left)
                {
                    parameters[kvp.Key] = kvp.Value;
                }
            }

            if (right != null)
            {
                foreach (var kvp in right)
                {
                    parameters[kvp.Key] = kvp.Value;
                }
            }

            return parameters;
        }

        private static BsonValue EvaluateVectorKnn(BsonValue left, BsonValue right)
        {
            // Placeholder implementation that falls back to VECTOR_DIST semantics until
            // the planner specializes VECTOR_KNN queries. Treats the operator as a
            // convenience alias for distance calculations.
            return VectorExpressions.VectorDistance(left, right);
        }
    }
}
