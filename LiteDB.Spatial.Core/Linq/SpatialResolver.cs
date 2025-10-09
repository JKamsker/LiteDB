#nullable enable

using System;
using System.Globalization;
using System.Linq.Expressions;

namespace LiteDB.Spatial;

/// <summary>
/// Translates method calls that originate from <see cref="SpatialExpressions"/> into spatial query plans.
/// </summary>
public sealed class SpatialResolver
{
    private readonly Func<SpatialCollectionDescriptor> _descriptorProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialResolver"/> class using a descriptor provider.
    /// </summary>
    /// <param name="descriptorProvider">Function that returns the spatial descriptor for the active collection.</param>
    public SpatialResolver(Func<SpatialCollectionDescriptor> descriptorProvider)
    {
        _descriptorProvider = descriptorProvider ?? throw new ArgumentNullException(nameof(descriptorProvider));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialResolver"/> class for a fixed descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor associated with the current collection.</param>
    public SpatialResolver(SpatialCollectionDescriptor descriptor)
        : this(() => descriptor ?? throw new ArgumentNullException(nameof(descriptor)))
    {
    }

    /// <summary>
    /// Attempts to translate the provided method call expression into a spatial query plan.
    /// </summary>
    /// <param name="expression">The method call expression extracted from a LINQ query.</param>
    /// <param name="plan">When this method returns, contains the resulting query plan if translation succeeded.</param>
    /// <returns><c>true</c> when translation succeeded; otherwise, <c>false</c>.</returns>
    public bool TryResolve(MethodCallExpression expression, out ISpatialQueryPlan? plan)
    {
        if (expression == null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        plan = default;

        if (expression.Method.DeclaringType != typeof(SpatialExpressions))
        {
            return false;
        }

        var descriptor = _descriptorProvider();

        if (descriptor == null)
        {
            throw new SpatialMetadataException("Spatial metadata descriptor provider returned null.");
        }

        var engine = descriptor.Engine ?? throw new SpatialMetadataException(
            $"Collection '{descriptor.CollectionName}' is not bound to a spatial engine. Configure the collection using the appropriate Spatial.Use* helper before issuing spatial queries.");

        EnsureEngineCompatibility(descriptor, engine);

        return expression.Method.Name switch
        {
            nameof(SpatialExpressions.Near) => TryResolveNear(expression, descriptor, engine, out plan),
            nameof(SpatialExpressions.InBox) => TryResolveInBox(expression, descriptor, engine, out plan),
            _ => false
        };
    }

    private static bool TryResolveNear(MethodCallExpression expression, SpatialCollectionDescriptor descriptor, ISpatialEngine engine, out ISpatialQueryPlan? plan)
    {
        plan = null;

        var parameters = expression.Method.GetParameters();
        if (parameters.Length != 3)
        {
            return false;
        }

        var candidateType = parameters[0].ParameterType;

        if (candidateType == typeof(GeoPoint))
        {
            EnsureDimensions(descriptor, 2, "two-dimensional near queries");

            var center = EvaluateArgument<GeoPoint>(expression.Arguments[1], "center");
            var radius = EvaluateDouble(expression.Arguments[2], "radius");

            plan = engine.PlanNear(center, radius);
            return true;
        }

        if (candidateType == typeof(GeoPoint3D))
        {
            EnsureDimensions(descriptor, 3, "three-dimensional near queries");

            var center = EvaluateArgument<GeoPoint3D>(expression.Arguments[1], "center");
            var radius = EvaluateDouble(expression.Arguments[2], "radius");

            plan = engine.PlanNear(center, radius);
            return true;
        }

        return false;
    }

    private static bool TryResolveInBox(MethodCallExpression expression, SpatialCollectionDescriptor descriptor, ISpatialEngine engine, out ISpatialQueryPlan? plan)
    {
        plan = null;

        var parameters = expression.Method.GetParameters();
        if (parameters.Length != 2)
        {
            return false;
        }

        var candidateType = parameters[0].ParameterType;

        if (candidateType == typeof(GeoPoint))
        {
            EnsureDimensions(descriptor, 2, "two-dimensional bounding boxes");

            var bounds = EvaluateArgument<BoundingBox>(expression.Arguments[1], "bounds");
            descriptor.EnsureCompatible(bounds);

            plan = engine.PlanWithin(bounds);
            return true;
        }

        if (candidateType == typeof(GeoPoint3D))
        {
            EnsureDimensions(descriptor, 3, "three-dimensional bounding boxes");

            var bounds = EvaluateArgument<BoundingBox>(expression.Arguments[1], "bounds");
            descriptor.EnsureCompatible(bounds);

            plan = engine.PlanWithin(bounds);
            return true;
        }

        return false;
    }

    private static void EnsureEngineCompatibility(SpatialCollectionDescriptor descriptor, ISpatialEngine engine)
    {
        if (engine.Dimensions != descriptor.Dimensions)
        {
            throw new SpatialMetadataException($"Spatial engine '{engine.Name}' reports {engine.Dimensions} dimensions but descriptor for collection '{descriptor.CollectionName}' expects {descriptor.Dimensions}. Recreate the spatial index to repair the mismatch.");
        }

        if (!engine.Options.Equals(descriptor.Options))
        {
            throw new SpatialMetadataException($"Spatial engine '{engine.Name}' options differ from the persisted metadata for collection '{descriptor.CollectionName}'. Ensure the collection is configured using the same options when building queries.");
        }
    }

    private static void EnsureDimensions(SpatialCollectionDescriptor descriptor, int expected, string scenario)
    {
        if (descriptor.Dimensions != expected)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for {descriptor.Dimensions}D geometry but {scenario} require {expected}D metadata.");
        }
    }

    private static T EvaluateArgument<T>(Expression expression, string argumentName)
    {
        if (expression == null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        object? value;

        if (expression is ConstantExpression constant)
        {
            value = constant.Value;
        }
        else
        {
            try
            {
                var lambda = Expression.Lambda(expression);
                value = lambda.Compile().DynamicInvoke();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Spatial expression argument '{argumentName}' must be a constant or capture. {ex.Message}", ex);
            }
        }

        if (value is null)
        {
            throw new InvalidOperationException($"Spatial expression argument '{argumentName}' evaluated to null.");
        }

        if (value is T typed)
        {
            return typed;
        }

        throw new InvalidOperationException($"Spatial expression argument '{argumentName}' has incompatible type. Expected {typeof(T).Name} but found {value.GetType().Name}.");
    }

    private static double EvaluateDouble(Expression expression, string argumentName)
    {
        var value = EvaluateArgument<object>(expression, argumentName);

        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Spatial expression argument '{argumentName}' could not be converted to a floating-point value.", ex);
        }
    }
}
