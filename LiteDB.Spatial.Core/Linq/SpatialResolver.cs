#nullable enable

using System;
using System.Linq.Expressions;

namespace LiteDB.Spatial;

/// <summary>
/// Translates method calls that originate from <see cref="SpatialExpressions"/> into spatial query plans.
/// </summary>
public sealed class SpatialResolver
{
    private readonly SpatialCollectionDescriptor _descriptor;
    private readonly Func<SpatialCollectionDescriptor, ISpatialEngine>? _engineFactory;
    private ISpatialEngine? _cachedEngine;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialResolver"/> class.
    /// </summary>
    /// <param name="descriptor">The spatial collection descriptor associated with the query source.</param>
    /// <param name="engineFactory">Optional factory used to obtain a runtime engine instance when the descriptor is not already attached to one.</param>
    public SpatialResolver(SpatialCollectionDescriptor descriptor, Func<SpatialCollectionDescriptor, ISpatialEngine>? engineFactory = null)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _engineFactory = engineFactory;
    }

    /// <summary>
    /// Attempts to translate the provided method call expression into a spatial query plan.
    /// </summary>
    /// <param name="expression">The method call expression extracted from a LINQ query.</param>
    /// <param name="plan">When this method returns, contains the resulting query plan if translation succeeded.</param>
    /// <returns><c>true</c> when translation succeeded; otherwise, <c>false</c>.</returns>
    public bool TryResolve(MethodCallExpression expression, out ISpatialQueryPlan? plan)
    {
        if (expression is null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        plan = default;

        if (expression.Method.DeclaringType != typeof(SpatialExpressions))
        {
            return false;
        }

        return expression.Method.Name switch
        {
            nameof(SpatialExpressions.Near) => TryResolveNear(expression, out plan),
            nameof(SpatialExpressions.InBox) => TryResolveInBox(expression, out plan),
            _ => false
        };
    }

    private bool TryResolveNear(MethodCallExpression expression, out ISpatialQueryPlan? plan)
    {
        var parameters = expression.Method.GetParameters();
        if (parameters.Length != 3)
        {
            plan = default;
            return false;
        }

        var candidateType = parameters[0].ParameterType;
        var engine = ResolveEngine();

        if (candidateType == typeof(GeoPoint))
        {
            EnsureDimensions(2, "GeoPoint", engine);
            var center = EvaluateArgument<GeoPoint>(expression.Arguments[1], "center point");
            var radius = EvaluateArgument<double>(expression.Arguments[2], "radius");
            plan = engine.PlanNear(center, radius);
            return true;
        }

        if (candidateType == typeof(GeoPoint3D))
        {
            EnsureDimensions(3, "GeoPoint3D", engine);
            var center = EvaluateArgument<GeoPoint3D>(expression.Arguments[1], "center point");
            var radius = EvaluateArgument<double>(expression.Arguments[2], "radius");
            plan = engine.PlanNear(center, radius);
            return true;
        }

        throw new NotSupportedException($"SpatialExpressions.Near does not support candidate type '{candidateType}'.");
    }

    private bool TryResolveInBox(MethodCallExpression expression, out ISpatialQueryPlan? plan)
    {
        var parameters = expression.Method.GetParameters();
        if (parameters.Length != 2)
        {
            plan = default;
            return false;
        }

        var candidateType = parameters[0].ParameterType;
        var bounds = EvaluateArgument<BoundingBox>(expression.Arguments[1], "bounding box");
        _descriptor.EnsureCompatible(bounds);

        var engine = ResolveEngine();

        if (candidateType == typeof(GeoPoint))
        {
            EnsureDimensions(2, "GeoPoint", engine);
            plan = engine.PlanWithin(bounds);
            return true;
        }

        if (candidateType == typeof(GeoPoint3D))
        {
            EnsureDimensions(3, "GeoPoint3D", engine);
            plan = engine.PlanWithin(bounds);
            return true;
        }

        throw new NotSupportedException($"SpatialExpressions.InBox does not support candidate type '{candidateType}'.");
    }

    private ISpatialEngine ResolveEngine()
    {
        if (_cachedEngine is not null)
        {
            return _cachedEngine;
        }

        if (_descriptor.HasEngine)
        {
            _cachedEngine = _descriptor.Engine!;
        }
        else
        {
            if (_engineFactory is null)
            {
                throw new SpatialMetadataException($"Collection '{_descriptor.CollectionName}' does not have a runtime spatial engine attached. Provide an engine factory when creating the resolver or attach an engine to the descriptor using WithEngine().");
            }

            _cachedEngine = _engineFactory(_descriptor) ?? throw new SpatialMetadataException($"The engine factory did not return an engine for collection '{_descriptor.CollectionName}'.");
        }

        if (!string.Equals(_cachedEngine.Name, _descriptor.EngineName, StringComparison.Ordinal))
        {
            throw new SpatialMetadataException($"Spatial engine '{_cachedEngine.Name}' does not match descriptor engine '{_descriptor.EngineName}'.");
        }

        if (_cachedEngine.Dimensions != _descriptor.Dimensions)
        {
            throw new SpatialMetadataException($"Spatial engine '{_cachedEngine.Name}' reports {_cachedEngine.Dimensions} dimensions but descriptor expects {_descriptor.Dimensions}.");
        }

        if (!_cachedEngine.Options.Equals(_descriptor.Options))
        {
            throw new SpatialMetadataException($"Spatial engine '{_cachedEngine.Name}' options do not match the descriptor options for collection '{_descriptor.CollectionName}'.");
        }

        return _cachedEngine;
    }

    private void EnsureDimensions(int expected, string argument, ISpatialEngine engine)
    {
        if (_descriptor.Dimensions != expected)
        {
            throw new SpatialMetadataException($"Collection '{_descriptor.CollectionName}' is configured for {_descriptor.Dimensions}D geometry but a {argument} was used which requires {expected}D metadata.");
        }

        if (engine.Dimensions != expected)
        {
            throw new SpatialMetadataException($"Spatial engine '{engine.Name}' reports {engine.Dimensions} dimensions but a {argument} requires {expected}D support.");
        }
    }

    private static T EvaluateArgument<T>(Expression expression, string description)
    {
        if (expression is null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        if (ContainsParameter(expression))
        {
            throw new NotSupportedException($"SpatialExpressions arguments must be constant or captured values. Unable to evaluate {description}.");
        }

        try
        {
            var lambda = Expression.Lambda<Func<T>>(Expression.Convert(expression, typeof(T)));
            return lambda.Compile().Invoke();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to evaluate spatial argument '{description}'.", ex);
        }
    }

    private static bool ContainsParameter(Expression expression)
    {
        var visitor = new ParameterDetectionVisitor();
        visitor.Visit(expression);
        return visitor.ContainsParameter;
    }

    private sealed class ParameterDetectionVisitor : ExpressionVisitor
    {
        public bool ContainsParameter { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            ContainsParameter = true;
            return base.VisitParameter(node);
        }
    }
}
