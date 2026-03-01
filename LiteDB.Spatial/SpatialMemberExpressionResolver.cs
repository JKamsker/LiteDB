#nullable enable

using System;
using System.Linq.Expressions;
using System.Reflection;

namespace LiteDB.Spatial
{
    internal static class SpatialMemberExpressionResolver
    {
        public static MemberInfo GetMemberInfo<T, TValue>(Expression<Func<T, TValue>> expression)
        {
            if (expression == null)
            {
                throw new ArgumentNullException(nameof(expression));
            }

            Expression current = expression.Body;

            while (current is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            {
                current = unary.Operand;
            }

            if (current is MemberExpression memberExpression)
            {
                return memberExpression.Member;
            }

            throw new ArgumentException("Spatial configuration expressions must target a field or property.", nameof(expression));
        }
    }
}
