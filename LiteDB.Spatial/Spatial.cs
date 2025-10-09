extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Provides the top-level spatial facade responsible for configuring collections and dispatching queries.
/// </summary>
public static class Spatial
{
    /// <summary>
    /// Configures the provided collection for geographic (WGS84) indexing and persists the metadata descriptor.
    /// </summary>
    public static SpatialCollectionDescriptor UseGeographic<T>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint>> selector,
        SpatialIndexOptions? options = null,
        GeographicDistanceMode distanceMode = GeographicDistanceMode.Haversine)
    {
        if (collection == null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        if (selector == null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        var fieldPath = GetFieldPath(selector);
        var resources = CreateResources(collection);
        var descriptor = SpatialGeographic.EnsurePointIndex(
            resources.Metadata,
            resources.Documents,
            fieldPath,
            options,
            distanceMode);

        EnsureNumericIndex(resources.TypedCollection, descriptor.Options);
        return descriptor;
    }

    /// <summary>
    /// Configures the collection for two-dimensional Cartesian indexing.
    /// </summary>
    public static SpatialCollectionDescriptor UseCartesian2D<T>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint>> selector,
        BoundingBox domain,
        SpatialIndexOptions? options = null)
    {
        if (collection == null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        if (selector == null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        if (domain.Dimensions != 2)
        {
            throw new ArgumentException("Cartesian2D domains must be two-dimensional.", nameof(domain));
        }

        var fieldPath = GetFieldPath(selector);
        var resources = CreateResources(collection);
        var descriptor = SpatialCartesian2D.EnsurePointIndex(
            resources.Metadata,
            resources.Documents,
            fieldPath,
            domain,
            options);

        EnsureNumericIndex(resources.TypedCollection, descriptor.Options);
        return descriptor;
    }

    /// <summary>
    /// Configures the collection for three-dimensional Cartesian indexing.
    /// </summary>
    public static SpatialCollectionDescriptor UseCartesian3D<T>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint3D>> selector,
        BoundingBox domain,
        SpatialIndexOptions? options = null)
    {
        if (collection == null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        if (selector == null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        if (domain.Dimensions != 3)
        {
            throw new ArgumentException("Cartesian3D domains must be three-dimensional.", nameof(domain));
        }

        var fieldPath = GetFieldPath(selector);
        var resources = CreateResources(collection);
        var descriptor = SpatialCartesian3D.EnsurePointIndex(
            resources.Metadata,
            resources.Documents,
            fieldPath,
            domain,
            options);

        EnsureNumericIndex(resources.TypedCollection, descriptor.Options);
        return descriptor;
    }

    /// <summary>
    /// Rebuilds the spatial index for the configured collection.
    /// </summary>
    public static SpatialCollectionDescriptor EnsurePointIndex<T>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint>> selector)
    {
        return EnsurePointIndexInternal(collection, selector);
    }

    /// <summary>
    /// Rebuilds the spatial index for the configured collection.
    /// </summary>
    public static SpatialCollectionDescriptor EnsurePointIndex<T>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint3D>> selector)
    {
        return EnsurePointIndexInternal(collection, selector);
    }

    /// <summary>
    /// Executes a radius query against the configured collection using the stored spatial metadata.
    /// </summary>
    public static IReadOnlyList<T> Near<T>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint>> selector,
        GeoPoint center,
        double radius,
        int? limit = null)
    {
        if (radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be non-negative.");
        }

        var resources = CreateResources(collection);
        var descriptor = GetRequiredDescriptor(resources, "Configure the collection using Spatial.UseGeographic or Spatial.UseCartesian2D before issuing queries.");
        descriptor.EnsureCompatible(BoundingBox.From2D(center.Longitude, center.Latitude, center.Longitude, center.Latitude));

        var engine = CreateEngine(descriptor);
        var plan = engine.PlanNear(center, radius);
        return ExecuteNearQuery(
            resources.TypedCollection,
            selector.Compile(),
            plan,
            engine.Distance,
            center,
            radius,
            descriptor.Options.DistanceTolerance,
            limit,
            descriptor.EngineName == GeographicEngine.EngineName);
    }

    /// <summary>
    /// Executes a radius query against the configured three-dimensional collection.
    /// </summary>
    public static IReadOnlyList<T> Near<T>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint3D>> selector,
        GeoPoint3D center,
        double radius,
        int? limit = null)
    {
        if (radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be non-negative.");
        }

        var resources = CreateResources(collection);
        var descriptor = GetRequiredDescriptor(resources, "Call Spatial.UseCartesian3D(collection, ...) before issuing queries.");
        descriptor.EnsureCompatible(BoundingBox.From3D(center.X, center.Y, center.Z, center.X, center.Y, center.Z));

        var engine = CreateEngine(descriptor);
        var plan = engine.PlanNear(center, radius);
        return ExecuteNearQuery(
            resources.TypedCollection,
            selector.Compile(),
            plan,
            engine.Distance,
            center,
            radius,
            descriptor.Options.DistanceTolerance,
            limit,
            allowLongitudeWrap: false);
    }

    /// <summary>
    /// Retrieves all items whose coordinates fall within the provided bounding box.
    /// </summary>
    public static IReadOnlyList<T> WithinBoundingBox<T>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint>> selector,
        BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Bounding boxes for geographic/Cartesian2D queries must be two-dimensional.", nameof(bounds));
        }

        var resources = CreateResources(collection);
        var descriptor = GetRequiredDescriptor(resources, "Configure the collection using Spatial.UseGeographic or Spatial.UseCartesian2D before issuing queries.");
        descriptor.EnsureCompatible(bounds);

        var engine = CreateEngine(descriptor);
        engine.PlanWithin(bounds);

        var allowLongitudeWrap = descriptor.EngineName == GeographicEngine.EngineName;

        var getter = selector.Compile();
        var results = new List<T>();

        foreach (var item in resources.TypedCollection.FindAll())
        {
            var point = getter(item);
            if (Contains(bounds, point, allowLongitudeWrap))
            {
                results.Add(item);
            }
        }

        return results;
    }

    /// <summary>
    /// Retrieves all items whose coordinates fall within the provided bounding box.
    /// </summary>
    public static IReadOnlyList<T> WithinBoundingBox<T>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint3D>> selector,
        BoundingBox bounds)
    {
        if (bounds.Dimensions != 3)
        {
            throw new ArgumentException("Bounding boxes for Cartesian3D queries must be three-dimensional.", nameof(bounds));
        }

        var resources = CreateResources(collection);
        var descriptor = GetRequiredDescriptor(resources, "Call Spatial.UseCartesian3D(collection, ...) before issuing queries.");
        descriptor.EnsureCompatible(bounds);

        var engine = CreateEngine(descriptor);
        engine.PlanWithin(bounds);

        var getter = selector.Compile();
        var results = new List<T>();

        foreach (var item in resources.TypedCollection.FindAll())
        {
            var point = getter(item);
            if (Contains(bounds, point))
            {
                results.Add(item);
            }
        }

        return results;
    }

    private static SpatialCollectionDescriptor EnsurePointIndexInternal<T, TValue>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, TValue>> selector)
    {
        if (collection == null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        if (selector == null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        var resources = CreateResources(collection);
        var descriptor = GetRequiredDescriptor(resources, "Configure the collection using Spatial.Use* before rebuilding the index.");
        var fieldPath = GetFieldPath(selector);

        if (!fieldPath.Equals(descriptor.GeometryFieldName, StringComparison.OrdinalIgnoreCase))
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for geometry field '{descriptor.GeometryFieldName}' but '{fieldPath}' was specified.");
        }

        SpatialCollectionDescriptor refreshed;

        if (descriptor.EngineName == GeographicEngine.EngineName)
        {
            var mode = descriptor.Settings.DistanceMode ?? GeographicDistanceMode.Haversine;
            refreshed = SpatialGeographic.EnsurePointIndex(resources.Metadata, resources.Documents, fieldPath, descriptor.Options, mode);
        }
        else if (descriptor.EngineName == Cartesian2DEngine.EngineName)
        {
            var domain = descriptor.Settings.Domain ?? throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is missing Cartesian domain metadata. Reconfigure using Spatial.UseCartesian2D.");
            refreshed = SpatialCartesian2D.EnsurePointIndex(resources.Metadata, resources.Documents, fieldPath, domain, descriptor.Options);
        }
        else if (descriptor.EngineName == Cartesian3DEngine.EngineName)
        {
            var domain = descriptor.Settings.Domain ?? throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is missing Cartesian domain metadata. Reconfigure using Spatial.UseCartesian3D.");
            refreshed = SpatialCartesian3D.EnsurePointIndex(resources.Metadata, resources.Documents, fieldPath, domain, descriptor.Options);
        }
        else
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for unknown engine '{descriptor.EngineName}'.");
        }

        EnsureNumericIndex(resources.TypedCollection, refreshed.Options);
        return refreshed;
    }

    private static IReadOnlyList<T> ExecuteNearQuery<T, TPoint>(
        BaseLiteDB.LiteCollection<T> collection,
        Func<T, TPoint> accessor,
        ISpatialQueryPlan plan,
        ISpatialDistance distance,
        TPoint center,
        double radius,
        double distanceTolerance,
        int? limit,
        bool allowLongitudeWrap)
    {
        var matches = new List<(T Item, double Distance)>();

        foreach (var item in collection.FindAll())
        {
            var point = accessor(item);
            if (point == null)
            {
                continue;
            }

            var wrappingEnabled = allowLongitudeWrap && plan.Dimensions == 2;
            var boundingTolerance = wrappingEnabled ? 0d : distanceTolerance;

            if (!IsWithinCovering(plan.CoveringBounds, point, wrappingEnabled, boundingTolerance))
            {
                continue;
            }

            var valueDistance = ComputeDistance(distance, point, center);
            if (valueDistance <= radius + distanceTolerance)
            {
                matches.Add((item, valueDistance));
            }
        }

        matches.Sort((left, right) => left.Distance.CompareTo(right.Distance));

        if (limit.HasValue)
        {
            return matches.Take(limit.Value).Select(x => x.Item).ToList();
        }

        return matches.Select(x => x.Item).ToList();
    }

    private static bool IsWithinCovering(BoundingBox? covering, object point, bool allowLongitudeWrap, double tolerance)
    {
        if (!covering.HasValue)
        {
            return true;
        }

        var bounds = covering.Value;

        if (point is GeoPoint geo2D)
        {
            var expanded = allowLongitudeWrap ? bounds : ExpandBoundingBox(bounds, tolerance, is3D: false);
            return Contains(expanded, geo2D, allowLongitudeWrap);
        }

        if (point is GeoPoint3D geo3D)
        {
            var expanded = ExpandBoundingBox(bounds, tolerance, is3D: true);
            return Contains(expanded, geo3D);
        }

        return false;
    }

    private static double ComputeDistance<TPoint>(ISpatialDistance distance, TPoint candidate, TPoint center)
    {
        if (candidate is GeoPoint point2D && center is GeoPoint center2D)
        {
            return distance.Distance(point2D, center2D);
        }

        if (candidate is GeoPoint3D point3D && center is GeoPoint3D center3D)
        {
            return distance.Distance(point3D, center3D);
        }

        throw new SpatialMetadataException("Unsupported coordinate type for distance computation.");
    }

    private static bool Contains(BoundingBox bounds, GeoPoint point, bool allowLongitudeWrap)
    {
        if (point.Latitude < bounds.MinY || point.Latitude > bounds.MaxY)
        {
            return false;
        }

        if (!allowLongitudeWrap)
        {
            return point.Longitude >= bounds.MinX && point.Longitude <= bounds.MaxX;
        }

        return ContainsLongitude(bounds.MinX, bounds.MaxX, point.Longitude);
    }

    private static bool ContainsLongitude(double minLongitude, double maxLongitude, double value)
    {
        if (maxLongitude - minLongitude >= 360d)
        {
            return true;
        }

        var normalizedMin = NormalizeLongitude(minLongitude);
        var offset = normalizedMin - minLongitude;
        var normalizedMax = maxLongitude + offset;
        var normalizedValue = NormalizeLongitude(value + offset);

        if (normalizedMax <= 180d)
        {
            return normalizedValue >= normalizedMin && normalizedValue <= normalizedMax;
        }

        var inUpperSegment = normalizedValue >= normalizedMin && normalizedValue <= 180d;
        var inLowerSegment = normalizedValue >= -180d && normalizedValue <= normalizedMax - 360d;
        return inUpperSegment || inLowerSegment;
    }

    private static double NormalizeLongitude(double longitude)
    {
        var normalized = longitude % 360d;

        if (normalized <= -180d)
        {
            normalized += 360d;
        }
        else if (normalized > 180d)
        {
            normalized -= 360d;
        }

        return normalized;
    }

    private static bool Contains(BoundingBox bounds, GeoPoint3D point)
    {
        return point.X >= bounds.MinX && point.X <= bounds.MaxX
            && point.Y >= bounds.MinY && point.Y <= bounds.MaxY
            && point.Z >= bounds.MinZ && point.Z <= bounds.MaxZ;
    }

    private static BoundingBox ExpandBoundingBox(BoundingBox bounds, double tolerance, bool is3D)
    {
        if (tolerance <= 0d)
        {
            return bounds;
        }

        if (!is3D)
        {
            return BoundingBox.From2D(
                bounds.MinX - tolerance,
                bounds.MinY - tolerance,
                bounds.MaxX + tolerance,
                bounds.MaxY + tolerance);
        }

        return BoundingBox.From3D(
            bounds.MinX - tolerance,
            bounds.MinY - tolerance,
            bounds.MinZ - tolerance,
            bounds.MaxX + tolerance,
            bounds.MaxY + tolerance,
            bounds.MaxZ + tolerance);
    }

    private static CollectionResources<T> CreateResources<T>(BaseLiteDB.ILiteCollection<T> collection)
    {
        var lite = GetLiteCollection(collection);
        var engine = GetEngine(lite) ?? throw new SpatialMetadataException("Unable to access the underlying engine for spatial operations.");
        var mapper = GetMapper(lite) ?? BaseLiteDB.BsonMapper.Global;
        var database = new BaseLiteDB.LiteDatabase(engine, mapper, disposeOnClose: false);
        return new CollectionResources<T>(lite, database);
    }

    private static SpatialCollectionDescriptor GetRequiredDescriptor<T>(CollectionResources<T> resources, string suggestion)
    {
        return resources.Metadata.GetRequiredDescriptor(resources.TypedCollection.Name, suggestion);
    }

    private static BaseLiteDB.LiteCollection<T> GetLiteCollection<T>(BaseLiteDB.ILiteCollection<T> collection)
    {
        if (collection is BaseLiteDB.LiteCollection<T> liteCollection)
        {
            return liteCollection;
        }

        throw new SpatialMetadataException("Spatial helpers require LiteCollection<T> instances.");
    }

    private static BaseLiteDB.Engine.ILiteEngine? GetEngine<T>(BaseLiteDB.LiteCollection<T> collection)
    {
        var field = typeof(BaseLiteDB.LiteCollection<T>).GetField("_engine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(collection) as BaseLiteDB.Engine.ILiteEngine;
    }

    private static BaseLiteDB.BsonMapper? GetMapper<T>(BaseLiteDB.LiteCollection<T> collection)
    {
        var field = typeof(BaseLiteDB.LiteCollection<T>).GetField("_mapper", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(collection) as BaseLiteDB.BsonMapper;
    }

    private static string GetFieldPath<T, TValue>(Expression<Func<T, TValue>> expression)
    {
        var current = StripConvert(expression.Body);
        var members = new Stack<string>();

        while (current is MemberExpression member)
        {
            members.Push(member.Member.Name);
            current = StripConvert(member.Expression);
        }

        if (members.Count == 0)
        {
            throw new SpatialMetadataException("Spatial operations require a member access expression identifying the geometry field.");
        }

        return string.Join(".", members);
    }

    private static Expression? StripConvert(Expression? expression)
    {
        if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            return unary.Operand;
        }

        return expression;
    }

    private static ISpatialEngine CreateEngine(SpatialCollectionDescriptor descriptor)
    {
        if (descriptor.TryGetEngine(out var engine))
        {
            return engine;
        }

        if (descriptor.EngineName == GeographicEngine.EngineName)
        {
            var mode = descriptor.Settings.DistanceMode ?? GeographicDistanceMode.Haversine;
            return new GeographicEngine(descriptor.GeometryFieldName, descriptor.Options, mode);
        }

        if (descriptor.EngineName == Cartesian2DEngine.EngineName)
        {
            var domain = descriptor.Settings.Domain ?? throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is missing Cartesian domain metadata. Reconfigure using Spatial.UseCartesian2D.");
            return new Cartesian2DEngine(descriptor.GeometryFieldName, domain, descriptor.Options);
        }

        if (descriptor.EngineName == Cartesian3DEngine.EngineName)
        {
            var domain = descriptor.Settings.Domain ?? throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is missing Cartesian domain metadata. Reconfigure using Spatial.UseCartesian3D.");
            return new Cartesian3DEngine(descriptor.GeometryFieldName, domain, descriptor.Options);
        }

        throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for unknown engine '{descriptor.EngineName}'.");
    }

    private static void EnsureNumericIndex<T>(BaseLiteDB.LiteCollection<T> collection, SpatialIndexOptions options)
    {
        var expression = BaseLiteDB.BsonExpression.Create("$." + options.IndexFieldName);
        collection.EnsureIndex(options.IndexFieldName, expression);
    }

    private sealed class CollectionResources<T>
    {
        public CollectionResources(BaseLiteDB.LiteCollection<T> typedCollection, BaseLiteDB.LiteDatabase database)
        {
            TypedCollection = typedCollection;
            Database = database;
            Metadata = new SpatialMetadataStore(database);
            Documents = database.GetCollection<BaseLiteDB.BsonDocument>(typedCollection.Name);
        }

        public BaseLiteDB.LiteCollection<T> TypedCollection { get; }

        public BaseLiteDB.LiteDatabase Database { get; }

        public SpatialMetadataStore Metadata { get; }

        public BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> Documents { get; }
    }
}
