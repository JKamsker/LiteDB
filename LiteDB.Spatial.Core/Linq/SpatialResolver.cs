#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace LiteDB.Spatial;

/// <summary>
/// Translates method calls that originate from <see cref="SpatialExpressions"/> into spatial query plans.
/// </summary>
public sealed class SpatialResolver
{
    private readonly IReadOnlyList<SpatialCollectionDescriptor> _descriptors;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialResolver"/> class.
    /// </summary>
    /// <param name="descriptor">The descriptor associated with the collection targeted by the query.</param>
    public SpatialResolver(SpatialCollectionDescriptor descriptor)
        : this(new[] { descriptor ?? throw new ArgumentNullException(nameof(descriptor)) })
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialResolver"/> class.
    /// </summary>
    /// <param name="descriptors">The known spatial descriptors for the query scope.</param>
    public SpatialResolver(IEnumerable<SpatialCollectionDescriptor> descriptors)
    {
        if (descriptors == null)
        {
            throw new ArgumentNullException(nameof(descriptors));
        }

        _descriptors = descriptors.ToArray();

        if (_descriptors.Count == 0)
        {
            throw new ArgumentException("At least one descriptor must be provided to resolve spatial expressions.", nameof(descriptors));
        }
    }

    /// <summary>
    /// Attempts to translate the provided method call expression into a spatial query plan.
    /// </summary>
    /// <param name="expression">The method call expression extracted from a LINQ query.</param>
    /// <param name="plan">When this method returns, contains the resulting query plan if translation succeeded.</param>
    /// <returns><c>true</c> when translation succeeded; otherwise, <c>false</c>.</returns>
    public bool TryResolve(MethodCallExpression expression, out ISpatialQueryPlan? plan)
    {
        plan = default;

        if (expression == null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        if (expression.Method.DeclaringType != typeof(SpatialExpressions))
        {
            return false;
        }

        return expression.Method.Name switch
        {
            nameof(SpatialExpressions.Near) => ResolveNear(expression, out plan),
            nameof(SpatialExpressions.InBox) => ResolveInBox(expression, out plan),
            _ => false
        };
    }

    private bool ResolveNear(MethodCallExpression expression, out ISpatialQueryPlan? plan)
    {
        plan = null;

        var descriptor = ResolveDescriptor(expression.Arguments[0]);
        var engine = RequireEngine(descriptor);

        var parameters = expression.Method.GetParameters();
        if (parameters.Length != 3)
        {
            throw new InvalidOperationException("SpatialExpressions.Near expects three parameters (candidate, center, radius).");
        }

        if (parameters[1].ParameterType == typeof(GeoPoint))
        {
            EnsureDimensions(descriptor, 2);
            var center = EvaluateArgument<GeoPoint>(expression.Arguments[1]);
            var radius = EvaluateArgument<double>(expression.Arguments[2]);
            plan = engine.PlanNear(center, radius);
            return true;
        }

        if (parameters[1].ParameterType == typeof(GeoPoint3D))
        {
            EnsureDimensions(descriptor, 3);
            var center = EvaluateArgument<GeoPoint3D>(expression.Arguments[1]);
            var radius = EvaluateArgument<double>(expression.Arguments[2]);
            plan = engine.PlanNear(center, radius);
            return true;
        }

        throw new NotSupportedException($"Unsupported Near overload: center parameter type '{parameters[1].ParameterType.Name}'.");
    }

    private bool ResolveInBox(MethodCallExpression expression, out ISpatialQueryPlan? plan)
    {
        plan = null;
        if (expression.Arguments.Count != 2)
        {
            throw new InvalidOperationException("SpatialExpressions.InBox expects two parameters (candidate, bounds).");
        }

        var descriptor = ResolveDescriptor(expression.Arguments[0]);
        var engine = RequireEngine(descriptor);
        var bounds = EvaluateArgument<BoundingBox>(expression.Arguments[1]);
        descriptor.EnsureCompatible(bounds);

        plan = engine.PlanWithin(bounds);
        return true;
    }

    private SpatialCollectionDescriptor ResolveDescriptor(Expression candidate)
    {
        var geometryName = TryExtractMemberName(candidate);

        if (geometryName != null)
        {
            foreach (var descriptor in _descriptors)
            {
                if (string.Equals(descriptor.GeometryFieldName, geometryName, StringComparison.OrdinalIgnoreCase))
                {
                    return descriptor;
                }
            }
        }

        if (_descriptors.Count == 1)
        {
            return _descriptors[0];
        }

        var available = string.Join(", ", _descriptors.Select(d => d.GeometryFieldName));
        throw new SpatialMetadataException($"Unable to resolve spatial metadata for geometry member '{geometryName ?? "<unknown>"}'. Known geometries: {available}.");
    }

    private static string? TryExtractMemberName(Expression expression)
    {
        Expression current = expression;

        while (current is UnaryExpression unary && (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked))
        {
            current = unary.Operand;
        }

        return current switch
        {
            MemberExpression member => member.Member.Name,
            _ => null
        };
    }

    private static ISpatialEngine RequireEngine(SpatialCollectionDescriptor descriptor)
    {
        if (descriptor.Engine is { } engine)
        {
            return engine;
        }

        throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' does not have a runtime spatial engine attached. Ensure EnsurePointIndex has been executed and the descriptor retains the engine via WithEngine().");
    }

    private static void EnsureDimensions(SpatialCollectionDescriptor descriptor, int expected)
    {
        if (descriptor.Dimensions != expected)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for {descriptor.Dimensions}D geometry but the query requested {expected}D semantics.");
        }
    }

    private static T EvaluateArgument<T>(Expression expression)
    {
        if (expression is ConstantExpression constant && constant.Value is T direct)
        {
            return direct;
        }

        var lambda = Expression.Lambda<Func<T>>(Expression.Convert(expression, typeof(T)));
        return lambda.Compile().Invoke();
    }
}
