extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using BaseLiteDB = LiteDbBase::LiteDB;
using BaseLiteEngine = LiteDbBase::LiteDB.Engine;
using LiteDbPlugins = LiteDbBase::LiteDB.Plugins;

namespace LiteDB.Spatial;

/// <summary>
/// Provides the top-level facade for configuring and executing spatial queries.
/// </summary>
public static class Spatial
{
    private static readonly ConditionalWeakTable<BaseLiteDB.LiteDatabase, SpatialDatabaseContext> _contexts = new();
    private static readonly ConcurrentDictionary<BaseLiteDB.BsonMapper, bool> _registeredMappers = new();

    /// <summary>
    /// Configures the collection to use the geographic engine and ensures the point index exists.
    /// </summary>
    public static SpatialCollectionDescriptor UseGeographic<T>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint>> geometrySelector,
        SpatialIndexOptions? options = null,
        GeographicDistanceMode distanceMode = GeographicDistanceMode.Haversine)
    {
        var liteCollection = EnsureLiteCollection(collection);
        var context = GetContext(liteCollection);
        var geometryField = ResolveGeometryField(liteCollection, geometrySelector);

        var descriptor = SpatialGeographic.EnsurePointIndex(
            context.Metadata,
            context.GetBsonCollection(liteCollection.Name),
            geometryField,
            options,
            distanceMode);

        EnsureIndexPresence(liteCollection, descriptor);
        return descriptor;
    }

    /// <summary>
    /// Configures the collection to use the geographic engine and ensures the point index exists.
    /// </summary>
    public static SpatialCollectionDescriptor UseGeographic(
        BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection,
        string geometryFieldName,
        SpatialIndexOptions? options = null,
        GeographicDistanceMode distanceMode = GeographicDistanceMode.Haversine)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        var liteCollection = EnsureLiteCollection(collection);
        var context = GetContext(liteCollection);

        var descriptor = SpatialGeographic.EnsurePointIndex(
            context.Metadata,
            context.GetBsonCollection(liteCollection.Name),
            geometryFieldName,
            options,
            distanceMode);

        EnsureIndexPresence(liteCollection, descriptor);
        return descriptor;
    }

    /// <summary>
    /// Configures the collection to use the two-dimensional Cartesian engine and ensures the point index exists.
    /// </summary>
    public static SpatialCollectionDescriptor UseCartesian2D<T>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint>> geometrySelector,
        BoundingBox domain,
        SpatialIndexOptions? options = null)
    {
        if (domain.Dimensions != 2)
        {
            throw new ArgumentException("Cartesian2D domains must describe two dimensions.", nameof(domain));
        }

        var liteCollection = EnsureLiteCollection(collection);
        var context = GetContext(liteCollection);
        var geometryField = ResolveGeometryField(liteCollection, geometrySelector);

        var descriptor = SpatialCartesian2D.EnsurePointIndex(
            context.Metadata,
            context.GetBsonCollection(liteCollection.Name),
            geometryField,
            domain,
            options);

        EnsureIndexPresence(liteCollection, descriptor);
        return descriptor;
    }

    /// <summary>
    /// Configures the collection to use the two-dimensional Cartesian engine and ensures the point index exists.
    /// </summary>
    public static SpatialCollectionDescriptor UseCartesian2D(
        BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection,
        string geometryFieldName,
        BoundingBox domain,
        SpatialIndexOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        if (domain.Dimensions != 2)
        {
            throw new ArgumentException("Cartesian2D domains must describe two dimensions.", nameof(domain));
        }

        var liteCollection = EnsureLiteCollection(collection);
        var context = GetContext(liteCollection);

        var descriptor = SpatialCartesian2D.EnsurePointIndex(
            context.Metadata,
            context.GetBsonCollection(liteCollection.Name),
            geometryFieldName,
            domain,
            options);

        EnsureIndexPresence(liteCollection, descriptor);
        return descriptor;
    }

    /// <summary>
    /// Configures the collection to use the three-dimensional Cartesian engine and ensures the point index exists.
    /// </summary>
    public static SpatialCollectionDescriptor UseCartesian3D<T>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint3D>> geometrySelector,
        BoundingBox domain,
        SpatialIndexOptions? options = null)
    {
        if (domain.Dimensions != 3)
        {
            throw new ArgumentException("Cartesian3D domains must describe three dimensions.", nameof(domain));
        }

        var liteCollection = EnsureLiteCollection(collection);
        var context = GetContext(liteCollection);
        var geometryField = ResolveGeometryField(liteCollection, geometrySelector);

        var descriptor = SpatialCartesian3D.EnsurePointIndex(
            context.Metadata,
            context.GetBsonCollection(liteCollection.Name),
            geometryField,
            domain,
            options);

        EnsureIndexPresence(liteCollection, descriptor);
        return descriptor;
    }

    /// <summary>
    /// Configures the collection to use the three-dimensional Cartesian engine and ensures the point index exists.
    /// </summary>
    public static SpatialCollectionDescriptor UseCartesian3D(
        BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection,
        string geometryFieldName,
        BoundingBox domain,
        SpatialIndexOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        if (domain.Dimensions != 3)
        {
            throw new ArgumentException("Cartesian3D domains must describe three dimensions.", nameof(domain));
        }

        var liteCollection = EnsureLiteCollection(collection);
        var context = GetContext(liteCollection);

        var descriptor = SpatialCartesian3D.EnsurePointIndex(
            context.Metadata,
            context.GetBsonCollection(liteCollection.Name),
            geometryFieldName,
            domain,
            options);

        EnsureIndexPresence(liteCollection, descriptor);
        return descriptor;
    }

    /// <summary>
    /// Ensures the spatial point index is present according to the stored metadata.
    /// </summary>
    public static SpatialCollectionDescriptor EnsurePointIndex<T>(BaseLiteDB.ILiteCollection<T> collection)
    {
        var liteCollection = EnsureLiteCollection(collection);
        var context = GetContext(liteCollection);
        var descriptor = context.Metadata.GetRequiredDescriptor(liteCollection.Name, "Call Spatial.UseGeographic(...) or the appropriate Spatial.Use* helper before ensuring the index.");

        var refreshed = EnsurePointIndexInternal(liteCollection, context, descriptor);
        return refreshed;
    }

    /// <summary>
    /// Ensures the spatial point index is present according to the stored metadata.
    /// </summary>
    public static SpatialCollectionDescriptor EnsurePointIndex(BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection)
    {
        var liteCollection = EnsureLiteCollection(collection);
        var context = GetContext(liteCollection);
        var descriptor = context.Metadata.GetRequiredDescriptor(liteCollection.Name, "Call Spatial.UseGeographic(...) or the appropriate Spatial.Use* helper before ensuring the index.");

        var refreshed = EnsurePointIndexInternal(liteCollection, context, descriptor);
        return refreshed;
    }

    /// <summary>
    /// Executes a near query for two-dimensional points.
    /// </summary>
    public static IReadOnlyList<T> Near<T>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint>> geometrySelector,
        GeoPoint center,
        double radius,
        int? limit = null)
    {
        var liteCollection = EnsureLiteCollection(collection);
        var context = GetContext(liteCollection);
        var descriptor = context.Metadata.GetRequiredDescriptor(liteCollection.Name, "Configure the collection with Spatial.UseGeographic or Spatial.UseCartesian2D before issuing spatial queries.");

        if (descriptor.Dimensions != 2)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for {descriptor.Dimensions}D geometry but a two-dimensional near query was requested.");
        }

        var engine = CreateEngine(descriptor);
        var plan = engine.PlanNear(center, radius);
        var predicate = BuildPredicate(descriptor, plan);
        var compiled = geometrySelector.Compile();
        var tolerance = descriptor.Options.DistanceTolerance;
        var source = FetchCandidates(liteCollection, predicate);
        var allowWrap = string.Equals(descriptor.EngineName, GeographicEngine.EngineName, StringComparison.Ordinal);

        var matches = new List<(T Item, double Distance)>();

        foreach (var candidate in source)
        {
            var point = compiled(candidate);
            var bounding = engine.Mapper.GetBoundingBox(point);

            if (!allowWrap && !BoundingBoxContains(point, plan.CoveringBounds ?? bounding, false))
            {
                continue;
            }

            if (allowWrap && plan.CoveringBounds.HasValue && !BoundingBoxContains(point, plan.CoveringBounds.Value, true))
            {
                continue;
            }

            var distance = engine.Distance.Distance(point, center);
            if (distance <= radius + tolerance)
            {
                matches.Add((candidate, distance));
            }
        }

        matches.Sort((left, right) => left.Distance.CompareTo(right.Distance));

        if (limit.HasValue && limit.Value >= 0 && matches.Count > limit.Value)
        {
            matches.RemoveRange(limit.Value, matches.Count - limit.Value);
        }

        return matches.Select(x => x.Item).ToList();
    }

    /// <summary>
    /// Executes a near query for three-dimensional points.
    /// </summary>
    public static IReadOnlyList<T> Near<T>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint3D>> geometrySelector,
        GeoPoint3D center,
        double radius,
        int? limit = null)
    {
        var liteCollection = EnsureLiteCollection(collection);
        var context = GetContext(liteCollection);
        var descriptor = context.Metadata.GetRequiredDescriptor(liteCollection.Name, "Configure the collection with Spatial.UseCartesian3D before issuing three-dimensional spatial queries.");

        if (descriptor.Dimensions != 3)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for {descriptor.Dimensions}D geometry but a three-dimensional near query was requested.");
        }

        var engine = CreateEngine(descriptor);
        var plan = engine.PlanNear(center, radius);
        var predicate = BuildPredicate(descriptor, plan);
        var compiled = geometrySelector.Compile();
        var tolerance = descriptor.Options.DistanceTolerance;
        var source = FetchCandidates(liteCollection, predicate);

        var matches = new List<(T Item, double Distance)>();

        foreach (var candidate in source)
        {
            var point = compiled(candidate);
            var bounding = engine.Mapper.GetBoundingBox(point);

            if (plan.CoveringBounds.HasValue && !BoundingBoxContains(point, plan.CoveringBounds.Value, false))
            {
                continue;
            }

            var distance = engine.Distance.Distance(point, center);
            if (distance <= radius + tolerance)
            {
                matches.Add((candidate, distance));
            }
        }

        matches.Sort((left, right) => left.Distance.CompareTo(right.Distance));

        if (limit.HasValue && limit.Value >= 0 && matches.Count > limit.Value)
        {
            matches.RemoveRange(limit.Value, matches.Count - limit.Value);
        }

        return matches.Select(x => x.Item).ToList();
    }

    /// <summary>
    /// Returns the documents whose points fall within the provided bounding box.
    /// </summary>
    public static IReadOnlyList<T> WithinBoundingBox<T>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint>> geometrySelector,
        BoundingBox bounds)
    {
        var liteCollection = EnsureLiteCollection(collection);
        var context = GetContext(liteCollection);
        var descriptor = context.Metadata.GetRequiredDescriptor(liteCollection.Name, "Configure the collection with Spatial.UseGeographic or Spatial.UseCartesian2D before issuing spatial queries.");

        descriptor.EnsureCompatible(bounds);

        if (descriptor.Dimensions != 2)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for {descriptor.Dimensions}D geometry but a two-dimensional bounding box query was requested.");
        }

        var engine = CreateEngine(descriptor);
        var plan = engine.PlanWithin(bounds);
        var predicate = BuildPredicate(descriptor, plan);
        var compiled = geometrySelector.Compile();
        var allowWrap = string.Equals(descriptor.EngineName, GeographicEngine.EngineName, StringComparison.Ordinal);
        var source = FetchCandidates(liteCollection, predicate);
        var matches = new List<T>();

        foreach (var candidate in source)
        {
            var point = compiled(candidate);
            if (BoundingBoxContains(point, bounds, allowWrap))
            {
                matches.Add(candidate);
            }
        }

        return matches;
    }

    /// <summary>
    /// Returns the documents whose three-dimensional points fall within the provided bounding box.
    /// </summary>
    public static IReadOnlyList<T> WithinBoundingBox<T>(
        BaseLiteDB.ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint3D>> geometrySelector,
        BoundingBox bounds)
    {
        var liteCollection = EnsureLiteCollection(collection);
        var context = GetContext(liteCollection);
        var descriptor = context.Metadata.GetRequiredDescriptor(liteCollection.Name, "Configure the collection with Spatial.UseCartesian3D before issuing three-dimensional spatial queries.");

        descriptor.EnsureCompatible(bounds);

        if (descriptor.Dimensions != 3)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for {descriptor.Dimensions}D geometry but a three-dimensional bounding box query was requested.");
        }

        var engine = CreateEngine(descriptor);
        var plan = engine.PlanWithin(bounds);
        var predicate = BuildPredicate(descriptor, plan);
        var compiled = geometrySelector.Compile();
        var source = FetchCandidates(liteCollection, predicate);
        var matches = new List<T>();

        foreach (var candidate in source)
        {
            var point = compiled(candidate);
            if (BoundingBoxContains(point, bounds, false))
            {
                matches.Add(candidate);
            }
        }

        return matches;
    }

    private static SpatialCollectionDescriptor EnsurePointIndexInternal<T>(
        BaseLiteDB.LiteCollection<T> collection,
        SpatialDatabaseContext context,
        SpatialCollectionDescriptor descriptor)
    {
        SpatialCollectionDescriptor refreshed = descriptor.EngineName switch
        {
            GeographicEngine.EngineName => SpatialGeographic.EnsurePointIndex(
                context.Metadata,
                context.GetBsonCollection(collection.Name),
                descriptor.GeometryFieldName,
                descriptor.Options,
                descriptor.Settings.DistanceMode ?? GeographicDistanceMode.Haversine),
            Cartesian2DEngine.EngineName => SpatialCartesian2D.EnsurePointIndex(
                context.Metadata,
                context.GetBsonCollection(collection.Name),
                descriptor.GeometryFieldName,
                descriptor.Settings.Domain ?? throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is missing domain metadata. Recreate the spatial index using Spatial.UseCartesian2D."),
                descriptor.Options),
            Cartesian3DEngine.EngineName => SpatialCartesian3D.EnsurePointIndex(
                context.Metadata,
                context.GetBsonCollection(collection.Name),
                descriptor.GeometryFieldName,
                descriptor.Settings.Domain ?? throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is missing domain metadata. Recreate the spatial index using Spatial.UseCartesian3D."),
                descriptor.Options),
            _ => throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for unknown engine '{descriptor.EngineName}'.")
        };

        EnsureIndexPresence(collection, refreshed);
        return refreshed;
    }

    private static BaseLiteDB.LiteCollection<T> EnsureLiteCollection<T>(BaseLiteDB.ILiteCollection<T> collection)
    {
        if (collection is BaseLiteDB.LiteCollection<T> liteCollection)
        {
            return liteCollection;
        }

        throw new NotSupportedException("Spatial helpers require LiteCollection<T> instances.");
    }

    private static SpatialDatabaseContext GetContext<T>(BaseLiteDB.LiteCollection<T> collection)
    {
        var databaseField = typeof(BaseLiteDB.LiteCollection<T>).GetField("_database", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (databaseField == null)
        {
            throw new InvalidOperationException("Unable to locate LiteCollection database field.");
        }

        var mapperField = typeof(BaseLiteDB.LiteCollection<T>).GetField("_mapper", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (mapperField == null)
        {
            throw new InvalidOperationException("Unable to locate LiteCollection mapper field.");
        }

        var database = (BaseLiteDB.LiteDatabase)databaseField.GetValue(collection)!;
        var mapper = (BaseLiteDB.BsonMapper)mapperField.GetValue(collection)!;

        EnsureMapperRegistration(mapper);

        return _contexts.GetValue(database, _ => new SpatialDatabaseContext(database));
    }

    private static void EnsureMapperRegistration(BaseLiteDB.BsonMapper mapper)
    {
        if (_registeredMappers.ContainsKey(mapper))
        {
            return;
        }

        lock (_registeredMappers)
        {
            if (_registeredMappers.ContainsKey(mapper))
            {
                return;
            }

            mapper.RegisterType(
                serialize: (GeoPoint point) => SerializeGeoPoint(point),
                deserialize: bson => DeserializeGeoPoint(bson));

            mapper.RegisterType(
                serialize: (GeoPoint3D point) => SerializeGeoPoint3D(point),
                deserialize: bson => DeserializeGeoPoint3D(bson));

            _registeredMappers[mapper] = true;
        }
    }

    private static BaseLiteDB.BsonValue SerializeGeoPoint(GeoPoint point)
    {
        var document = new BaseLiteDB.BsonDocument
        {
            ["longitude"] = point.Longitude,
            ["latitude"] = point.Latitude,
            ["lon"] = point.Longitude,
            ["lat"] = point.Latitude,
            ["x"] = point.Longitude,
            ["y"] = point.Latitude
        };

        return document;
    }

    private static GeoPoint DeserializeGeoPoint(BaseLiteDB.BsonValue bson)
    {
        if (bson.IsNull)
        {
            return default;
        }

        if (!bson.IsDocument)
        {
            throw new BaseLiteDB.LiteException(0, "Stored GeoPoint value must be a document.");
        }

        var document = bson.AsDocument;
        var longitude = ReadDouble(document, "longitude") ?? ReadDouble(document, "lon") ?? ReadDouble(document, "x");
        var latitude = ReadDouble(document, "latitude") ?? ReadDouble(document, "lat") ?? ReadDouble(document, "y");

        if (!longitude.HasValue || !latitude.HasValue)
        {
            throw new BaseLiteDB.LiteException(0, "Stored GeoPoint document is missing longitude or latitude components.");
        }

        return new GeoPoint(longitude.Value, latitude.Value);
    }

    private static BaseLiteDB.BsonValue SerializeGeoPoint3D(GeoPoint3D point)
    {
        var document = new BaseLiteDB.BsonDocument
        {
            ["x"] = point.X,
            ["y"] = point.Y,
            ["z"] = point.Z
        };

        return document;
    }

    private static GeoPoint3D DeserializeGeoPoint3D(BaseLiteDB.BsonValue bson)
    {
        if (bson.IsNull)
        {
            return default;
        }

        if (!bson.IsDocument)
        {
            throw new BaseLiteDB.LiteException(0, "Stored GeoPoint3D value must be a document.");
        }

        var document = bson.AsDocument;
        var x = ReadDouble(document, "x") ?? ReadDouble(document, "lon") ?? ReadDouble(document, "longitude");
        var y = ReadDouble(document, "y") ?? ReadDouble(document, "lat") ?? ReadDouble(document, "latitude");
        var z = ReadDouble(document, "z");

        if (!x.HasValue || !y.HasValue || !z.HasValue)
        {
            throw new BaseLiteDB.LiteException(0, "Stored GeoPoint3D document is missing coordinate components.");
        }

        return new GeoPoint3D(x.Value, y.Value, z.Value);
    }

    private static double? ReadDouble(BaseLiteDB.BsonDocument document, string field)
    {
        if (document.TryGetValue(field, out var value) && value.IsNumber)
        {
            return value.AsDouble;
        }

        return null;
    }

    private static string ResolveGeometryField<T, TGeometry>(BaseLiteDB.LiteCollection<T> collection, Expression<Func<T, TGeometry>> selector)
    {
        if (collection.EntityMapper == null)
        {
            throw new SpatialMetadataException("Geometry expressions require a typed entity. Use the overload that accepts a geometry field name for BsonDocument collections.");
        }

        collection.EntityMapper.WaitForInitialization();
        var member = collection.EntityMapper.GetMember(selector.Body);
        if (member == null)
        {
            throw new SpatialMetadataException("Unable to resolve geometry member from expression. Ensure the expression targets a mapped property.");
        }

        return member.FieldName;
    }

    private static IEnumerable<T> FetchCandidates<T>(BaseLiteDB.LiteCollection<T> collection, BaseLiteDB.BsonExpression? predicate)
    {
        if (predicate == null)
        {
            return collection.FindAll();
        }

        return collection.Find(predicate);
    }

    private static ISpatialEngine CreateEngine(SpatialCollectionDescriptor descriptor)
    {
        return descriptor.EngineName switch
        {
            GeographicEngine.EngineName => new GeographicEngine(
                descriptor.GeometryFieldName,
                descriptor.Options,
                descriptor.Settings.DistanceMode ?? GeographicDistanceMode.Haversine),
            Cartesian2DEngine.EngineName => new Cartesian2DEngine(
                descriptor.GeometryFieldName,
                descriptor.Settings.Domain ?? throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is missing domain metadata. Recreate the spatial index using Spatial.UseCartesian2D."),
                descriptor.Options),
            Cartesian3DEngine.EngineName => new Cartesian3DEngine(
                descriptor.GeometryFieldName,
                descriptor.Settings.Domain ?? throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is missing domain metadata. Recreate the spatial index using Spatial.UseCartesian3D."),
                descriptor.Options),
            _ => throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for unknown engine '{descriptor.EngineName}'.")
        };
    }

    private static BaseLiteDB.BsonExpression? BuildPredicate(SpatialCollectionDescriptor descriptor, ISpatialQueryPlan plan)
    {
        var indexExpression = BuildIndexExpression(descriptor, plan.IndexRanges);

        if (plan.CoveringBounds is null)
        {
            return indexExpression;
        }

        if (string.Equals(descriptor.EngineName, GeographicEngine.EngineName, StringComparison.Ordinal))
        {
            // Geographic bounds that straddle the anti-meridian are best handled in memory to avoid false negatives.
            return indexExpression;
        }

        var boundingExpression = BuildBoundingExpression(descriptor, plan.CoveringBounds.Value);
        return CombineExpressions(indexExpression, boundingExpression);
    }

    private static BaseLiteDB.BsonExpression? BuildIndexExpression(SpatialCollectionDescriptor descriptor, IReadOnlyList<SpatialIndexRange> ranges)
    {
        if (ranges.Count == 0)
        {
            return null;
        }

        var expressions = new List<BaseLiteDB.BsonExpression>(ranges.Count);
        var field = $"$.{descriptor.Options.IndexFieldName}";

        foreach (var range in ranges)
        {
            var start = CreateIndexValue(range.Start);
            var end = CreateIndexValue(range.End);
            expressions.Add(BaseLiteDB.Query.Between(field, start, end));
        }

        if (expressions.Count == 1)
        {
            return expressions[0];
        }

        return BaseLiteDB.Query.Or(expressions.ToArray());
    }

    private static BaseLiteDB.BsonExpression? BuildBoundingExpression(SpatialCollectionDescriptor descriptor, BoundingBox bounds)
    {
        var values = bounds.GetValues();
        var field = $"$.{descriptor.Options.BoundingBoxFieldName}";

        if (values.Length == 4)
        {
            var minX = new BaseLiteDB.BsonValue(values[0]);
            var maxX = new BaseLiteDB.BsonValue(values[2]);
            var minY = new BaseLiteDB.BsonValue(values[1]);
            var maxY = new BaseLiteDB.BsonValue(values[3]);

            var expression = $"({field} != null) AND {field}[0] <= {maxX} AND {field}[2] >= {minX} AND {field}[1] <= {maxY} AND {field}[3] >= {minY}";
            return BaseLiteDB.BsonExpression.Create(expression);
        }

        if (values.Length == 6)
        {
            var minX = new BaseLiteDB.BsonValue(values[0]);
            var maxX = new BaseLiteDB.BsonValue(values[3]);
            var minY = new BaseLiteDB.BsonValue(values[1]);
            var maxY = new BaseLiteDB.BsonValue(values[4]);
            var minZ = new BaseLiteDB.BsonValue(values[2]);
            var maxZ = new BaseLiteDB.BsonValue(values[5]);

            var expression = $"({field} != null) AND {field}[0] <= {maxX} AND {field}[3] >= {minX} AND {field}[1] <= {maxY} AND {field}[4] >= {minY} AND {field}[2] <= {maxZ} AND {field}[5] >= {minZ}";
            return BaseLiteDB.BsonExpression.Create(expression);
        }

        return null;
    }

    private static BaseLiteDB.BsonExpression? CombineExpressions(BaseLiteDB.BsonExpression? left, BaseLiteDB.BsonExpression? right)
    {
        if (left == null)
        {
            return right;
        }

        if (right == null)
        {
            return left;
        }

        var parameterValues = new List<BaseLiteDB.BsonValue>();
        var nextParameterIndex = 0;

        static string RewriteExpression(
            string source,
            BaseLiteDB.BsonDocument? parameters,
            List<BaseLiteDB.BsonValue> target,
            ref int nextIndex)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return source;
            }

            var map = new Dictionary<int, int>();

            foreach (var entry in parameters
                .Select(kvp => new { Key = kvp.Key, Value = kvp.Value })
                .Where(item => int.TryParse(item.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                .OrderBy(item => int.Parse(item.Key, CultureInfo.InvariantCulture)))
            {
                var originalIndex = int.Parse(entry.Key, CultureInfo.InvariantCulture);

                if (!map.ContainsKey(originalIndex))
                {
                    map[originalIndex] = nextIndex++;
                    target.Add(entry.Value);
                }
            }

            if (map.Count == 0)
            {
                return source;
            }

            return Regex.Replace(
                source,
                @"@(\d+)",
                match =>
                {
                    var original = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (map.TryGetValue(original, out var replacement))
                    {
                        return "@" + replacement.ToString(CultureInfo.InvariantCulture);
                    }

                    return match.Value;
                },
                RegexOptions.CultureInvariant);
        }

        var leftSource = RewriteExpression(left.Source, left.Parameters, parameterValues, ref nextParameterIndex);
        var rightSource = RewriteExpression(right.Source, right.Parameters, parameterValues, ref nextParameterIndex);

        return BaseLiteDB.BsonExpression.Create($"(({leftSource}) AND ({rightSource}))", parameterValues.ToArray());
    }

    private static BaseLiteDB.BsonValue CreateIndexValue(ulong value)
    {
        if (value <= long.MaxValue)
        {
            return new BaseLiteDB.BsonValue((long)value);
        }

        return new BaseLiteDB.BsonValue((decimal)value);
    }

    private static bool BoundingBoxContains(GeoPoint point, BoundingBox bounds, bool allowWrap)
    {
        var values = bounds.GetValues();
        var minX = values[0];
        var minY = values[1];
        var maxX = values[2];
        var maxY = values[3];

        if (point.Latitude < minY || point.Latitude > maxY)
        {
            return false;
        }

        if (!allowWrap)
        {
            return point.Longitude >= minX && point.Longitude <= maxX;
        }

        if (maxX - minX >= 360d)
        {
            return true;
        }

        var normalizedLongitude = NormalizeLongitude(point.Longitude);
        foreach (var segment in SplitLongitudeRange(minX, maxX))
        {
            if (normalizedLongitude >= segment.min && normalizedLongitude <= segment.max)
            {
                return true;
            }
        }

        return false;
    }

    private static bool BoundingBoxContains(GeoPoint3D point, BoundingBox bounds, bool allowWrap)
    {
        var values = bounds.GetValues();
        if (values.Length == 4)
        {
            return BoundingBoxContains(new GeoPoint(point.X, point.Y), bounds, allowWrap);
        }

        return point.X >= values[0]
            && point.X <= values[3]
            && point.Y >= values[1]
            && point.Y <= values[4]
            && point.Z >= values[2]
            && point.Z <= values[5];
    }

    private static double NormalizeLongitude(double longitude)
    {
        var value = longitude % 360d;
        if (value <= -180d)
        {
            value += 360d;
        }
        else if (value > 180d)
        {
            value -= 360d;
        }

        return value;
    }

    private static IReadOnlyList<(double min, double max)> SplitLongitudeRange(double min, double max)
    {
        if (max - min >= 360d)
        {
            return new[] { (-180d, 180d) };
        }

        var normalizedMin = NormalizeLongitude(min);
        var offset = normalizedMin - min;
        var normalizedMax = max + offset;

        if (normalizedMax <= 180d)
        {
            return new[] { (normalizedMin, normalizedMax) };
        }

        return new[]
        {
            (normalizedMin, 180d),
            (-180d, normalizedMax - 360d)
        };
    }

    private static void EnsureIndexPresence<T>(BaseLiteDB.LiteCollection<T> collection, SpatialCollectionDescriptor descriptor)
    {
        var options = descriptor.Options;
        if (options == null)
        {
            return;
        }

        var registry = collection.Database?.Services?.ExpressionRegistry;
        if (registry == null)
        {
            return;
        }

        EnsureIndexPresence(collection, options.IndexFieldName, registry);
        EnsureIndexPresence(collection, options.BoundingBoxFieldName, registry);
    }

    private static void EnsureIndexPresence<T>(BaseLiteDB.LiteCollection<T> collection, string fieldName, LiteDbPlugins.IExpressionRegistry registry)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        var indexExpression = BaseLiteDB.BsonExpression.Create($"$.{fieldName}", registry);
        collection.EnsureIndex(indexExpression);
    }

    private sealed class SpatialDatabaseContext
    {
        private readonly BaseLiteDB.ILiteDatabase _database;

        public SpatialDatabaseContext(BaseLiteDB.LiteDatabase database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            Metadata = new SpatialMetadataStore(database);
        }

        public SpatialMetadataStore Metadata { get; }

        public BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> GetBsonCollection(string name)
        {
            return _database.GetCollection(name);
        }
    }
}
