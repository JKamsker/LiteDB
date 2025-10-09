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
    private readonly Dictionary<string, SpatialCollectionDescriptor> _descriptorByField;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialResolver"/> class.
    /// </summary>
    /// <param name="descriptors">Optional descriptors that should be registered using their geometry field names.</param>
    public SpatialResolver(IEnumerable<SpatialCollectionDescriptor>? descriptors = null)
    {
        _descriptorByField = new Dictionary<string, SpatialCollectionDescriptor>(StringComparer.OrdinalIgnoreCase);

        if (descriptors != null)
        {
            foreach (var descriptor in descriptors)
            {
                RegisterDescriptor(descriptor);
            }
        }
    }

    /// <summary>
    /// Registers a descriptor for translation. The descriptor is always indexed by its geometry field name
    /// and may optionally be associated with additional field aliases that appear inside LINQ expressions.
    /// </summary>
    /// <param name="descriptor">The descriptor describing the target collection.</param>
    /// <param name="fieldAliases">Optional aliases that identify the spatial member inside LINQ expressions.</param>
    public void RegisterDescriptor(SpatialCollectionDescriptor descriptor, params string[]? fieldAliases)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            descriptor.GeometryFieldName
        };

        if (fieldAliases is { Length: > 0 })
        {
            foreach (var alias in fieldAliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    names.Add(alias);
                }
            }
        }

        foreach (var name in names)
        {
            var key = NormalizeFieldKey(name);
            _descriptorByField[key] = descriptor;
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
        if (expression == null)
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
        var descriptor = ResolveDescriptor(expression.Arguments[0]);
        var engine = ResolveEngine(descriptor);

        var parameters = expression.Method.GetParameters();
        if (parameters.Length < 3)
        {
            throw new SpatialMetadataException("SpatialExpressions.Near expects a candidate, center, and radius argument.");
        }

        var radius = Evaluate<double>(expression.Arguments[2]);
        var candidateType = parameters[0].ParameterType;

        if (candidateType == typeof(GeoPoint))
        {
            EnsureDimensions(descriptor, 2);
            var center = Evaluate<GeoPoint>(expression.Arguments[1]);
            plan = engine.PlanNear(center, radius);
            return true;
        }

        if (candidateType == typeof(GeoPoint3D))
        {
            EnsureDimensions(descriptor, 3);
            var center = Evaluate<GeoPoint3D>(expression.Arguments[1]);
            plan = engine.PlanNear(center, radius);
            return true;
        }

        throw new SpatialMetadataException($"SpatialExpressions.Near overload with candidate type '{candidateType.Name}' is not supported.");
    }

    private bool TryResolveInBox(MethodCallExpression expression, out ISpatialQueryPlan? plan)
    {
        var descriptor = ResolveDescriptor(expression.Arguments[0]);
        var engine = ResolveEngine(descriptor);
        var parameters = expression.Method.GetParameters();

        if (parameters.Length < 2)
        {
            throw new SpatialMetadataException("SpatialExpressions.InBox expects a candidate and bounding box argument.");
        }

        var candidateType = parameters[0].ParameterType;

        if (candidateType == typeof(GeoPoint))
        {
            EnsureDimensions(descriptor, 2);
        }
        else if (candidateType == typeof(GeoPoint3D))
        {
            EnsureDimensions(descriptor, 3);
        }
        else
        {
            throw new SpatialMetadataException($"SpatialExpressions.InBox overload with candidate type '{candidateType.Name}' is not supported.");
        }

        var bounds = Evaluate<BoundingBox>(expression.Arguments[1]);
        descriptor.EnsureCompatible(bounds);

        plan = engine.PlanWithin(bounds);
        return true;
    }

    private SpatialCollectionDescriptor ResolveDescriptor(Expression expression)
    {
        var fieldPath = GetFieldPath(expression);
        var key = NormalizeFieldKey(fieldPath);

        if (_descriptorByField.TryGetValue(key, out var descriptor))
        {
            return descriptor;
        }

        descriptor = _descriptorByField.Values.FirstOrDefault(d => NormalizeFieldKey(d.GeometryFieldName) == key);

        if (descriptor is null)
        {
            throw new SpatialMetadataException($"No spatial metadata registered for member '{fieldPath}'. Configure the collection using the appropriate Spatial.*.Ensure* helper or register the descriptor before issuing spatial queries.");
        }

        return descriptor;
    }

    private static string GetFieldPath(Expression expression)
    {
        var current = StripConvert(expression) ?? throw new SpatialMetadataException("SpatialExpressions require a member access identifying the spatial field.");

        var members = new Stack<string>();
        while (current is MemberExpression member)
        {
            members.Push(member.Member.Name);
            current = StripConvert(member.Expression);
        }

        if (members.Count == 0)
        {
            throw new SpatialMetadataException("SpatialExpressions require a member access identifying the spatial field.");
        }

        return string.Join(".", members);
    }

    private static Expression? StripConvert(Expression? expression)
    {
        while (expression is UnaryExpression unary && (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked))
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static string NormalizeFieldKey(string field)
    {
        return field
            .Replace("[", ".")
            .Replace("]", string.Empty)
            .Replace(" ", string.Empty)
            .Trim()
            .ToLowerInvariant();
    }

    private static void EnsureDimensions(SpatialCollectionDescriptor descriptor, int expected)
    {
        if (descriptor.Dimensions != expected)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for {descriptor.Dimensions}D geometry but the query expects {expected}D.");
        }
    }

    private static T Evaluate<T>(Expression expression)
    {
        try
        {
            var convert = Expression.Convert(expression, typeof(T));
            var lambda = Expression.Lambda<Func<T>>(convert);
            return lambda.Compile().Invoke();
        }
        catch (SpatialMetadataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SpatialMetadataException($"Spatial query arguments must be constant or captured values that can be evaluated at translation time. {ex.Message}");
        }
    }

    private static ISpatialEngine ResolveEngine(SpatialCollectionDescriptor descriptor)
    {
        if (descriptor.TryGetEngine(out var engine))
        {
            return engine;
        }

        throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for engine '{descriptor.EngineName}' but no runtime engine instance was registered. Attach an engine via descriptor.WithEngine or descriptor.WithEngineFactory before composing LINQ queries.");
    }
}
