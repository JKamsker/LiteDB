#nullable enable

using System.Linq.Expressions;

namespace LiteDB.Spatial;

/// <summary>
/// Translates method calls that originate from <see cref="SpatialExpressions"/> into spatial query plans.
/// </summary>
public sealed class SpatialResolver
{
    /// <summary>
    /// Attempts to translate the provided method call expression into a spatial query plan.
    /// </summary>
    /// <param name="expression">The method call expression extracted from a LINQ query.</param>
    /// <param name="plan">When this method returns, contains the resulting query plan if translation succeeded.</param>
    /// <returns><c>true</c> when translation succeeded; otherwise, <c>false</c>.</returns>
    public bool TryResolve(MethodCallExpression expression, out ISpatialQueryPlan? plan)
    {
        plan = default;
        return false;
    }
}
