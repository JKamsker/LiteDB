extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BaseLiteDB = LiteDbBase::LiteDB;
using BaseEngine = LiteDbBase::LiteDB.Engine;

namespace LiteDB.Spatial;

/// <summary>
/// Top-level entry point for configuring and querying spatial collections.
/// </summary>
public static class Spatial
{
    private const string GeographicSuggestion = "Call Spatial.UseGeographic(collection, options) before ensuring indexes.";
    private const string Cartesian2DSuggestion = "Call Spatial.UseCartesian2D(collection, domain, options) before ensuring indexes.";
    private const string Cartesian3DSuggestion = "Call Spatial.UseCartesian3D(collection, domain, options) before ensuring indexes.";
    private const int Cartesian3DDefaultPrecisionBits = 36;
    private static readonly Type EnumerableType = typeof(System.Collections.IEnumerable);

    private static readonly ConcurrentDictionary<(object Engine, string Name), SpatialMetadataStore> MetadataStores = new();

    /// <summary>
    /// Configures the target collection to use the geographic spatial engine.
    /// </summary>
    public static SpatialCollectionDescriptor UseGeographic<T>(BaseLiteDB.ILiteCollection<T> collection, SpatialIndexOptions? options = null, GeographicDistanceMode distanceMode = GeographicDistanceMode.Haversine)
    {
        var liteCollection = GetLiteCollection(collection);
        var metadata = GetMetadataStore(liteCollection);
        var effectiveOptions = options ?? new SpatialIndexOptions();
        var descriptor = new SpatialCollectionDescriptor(
            liteCollection.Name,
            GeographicEngine.EngineName,
            dimensions: 2,
            SpatialCollectionDescriptor.DefaultGeometryFieldName,
            effectiveOptions,
            SpatialEngineSettings.ForGeographic(distanceMode));

        metadata.SaveDescriptor(liteCollection.Name, descriptor);
        return descriptor;
    }

    /// <summary>
    /// Configures the target collection to use the Cartesian 2D spatial engine within the specified domain.
    /// </summary>
    public static SpatialCollectionDescriptor UseCartesian2D<T>(BaseLiteDB.ILiteCollection<T> collection, BoundingBox domain, SpatialIndexOptions? options = null)
    {
        if (domain.Dimensions != 2)
        {
            throw new ArgumentException("Cartesian2D engines require a two-dimensional domain.", nameof(domain));
        }

        var liteCollection = GetLiteCollection(collection);
        var metadata = GetMetadataStore(liteCollection);
        var effectiveOptions = options ?? new SpatialIndexOptions();
        var descriptor = new SpatialCollectionDescriptor(
            liteCollection.Name,
            Cartesian2DEngine.EngineName,
            dimensions: 2,
            SpatialCollectionDescriptor.DefaultGeometryFieldName,
            effectiveOptions,
            SpatialEngineSettings.ForCartesian(domain));

        metadata.SaveDescriptor(liteCollection.Name, descriptor);
        return descriptor;
    }

    /// <summary>
    /// Configures the target collection to use the Cartesian 3D spatial engine within the specified domain.
    /// </summary>
    public static SpatialCollectionDescriptor UseCartesian3D<T>(BaseLiteDB.ILiteCollection<T> collection, BoundingBox domain, SpatialIndexOptions? options = null)
    {
        if (domain.Dimensions != 3)
        {
            throw new ArgumentException("Cartesian3D engines require a three-dimensional domain.", nameof(domain));
        }

        var liteCollection = GetLiteCollection(collection);
        var metadata = GetMetadataStore(liteCollection);
        var effectiveOptions = options ?? new SpatialIndexOptions(precisionBits: Cartesian3DDefaultPrecisionBits);
        var descriptor = new SpatialCollectionDescriptor(
            liteCollection.Name,
            Cartesian3DEngine.EngineName,
            dimensions: 3,
            SpatialCollectionDescriptor.DefaultGeometryFieldName,
            effectiveOptions,
            SpatialEngineSettings.ForCartesian(domain));

        metadata.SaveDescriptor(liteCollection.Name, descriptor);
        return descriptor;
    }

    /// <summary>
    /// Ensures point index fields exist for the configured two-dimensional engine.
    /// </summary>
    public static SpatialCollectionDescriptor EnsurePointIndex<T>(BaseLiteDB.ILiteCollection<T> collection, Expression<Func<T, GeoPoint>> accessor, SpatialIndexOptions? options = null)
    {
        return EnsurePointIndexInternal(collection, accessor, options, expectedDimensions: 2);
    }

    /// <summary>
    /// Ensures point index fields exist for the configured three-dimensional engine.
    /// </summary>
    public static SpatialCollectionDescriptor EnsurePointIndex<T>(BaseLiteDB.ILiteCollection<T> collection, Expression<Func<T, GeoPoint3D>> accessor, SpatialIndexOptions? options = null)
    {
        return EnsurePointIndexInternal(collection, accessor, options, expectedDimensions: 3);
    }

    /// <summary>
    /// Executes a radius query against a configured two-dimensional spatial collection.
    /// </summary>
    public static IEnumerable<T> Near<T>(BaseLiteDB.ILiteCollection<T> collection, Expression<Func<T, GeoPoint>> accessor, GeoPoint center, double radius, int? limit = null)
    {
        var liteCollection = GetLiteCollection(collection);
        var descriptor = GetDescriptor(liteCollection, GeographicSuggestion, Cartesian2DSuggestion);
        EnsureDimensions(descriptor, 2);

        var engine = EnsureEngine(descriptor);
        var plan = descriptor.EngineName switch
        {
            GeographicEngine.EngineName => SpatialGeographic.Near(descriptor, center, radius),
            Cartesian2DEngine.EngineName => SpatialCartesian2D.Near(descriptor, center, radius),
            _ => throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for '{descriptor.EngineName}' which does not support two-dimensional Near queries.")
        };

        var predicate = BuildPredicate(descriptor, plan);
        var getter = accessor.Compile();
        var source = ExecuteQuery(liteCollection, predicate);
        var tolerance = descriptor.Options.DistanceTolerance;

        var results = new List<(T Item, double Distance)>();
        foreach (var item in source)
        {
            var point = getter(item);
            var distance = engine.Distance.Distance(point, center);
            if (distance <= radius + tolerance)
            {
                results.Add((item, distance));
            }
        }

        results.Sort((left, right) => left.Distance.CompareTo(right.Distance));

        return limit.HasValue
            ? results.Take(limit.Value).Select(tuple => tuple.Item).ToList()
            : results.Select(tuple => tuple.Item).ToList();
    }

    /// <summary>
    /// Executes a radius query against a configured three-dimensional spatial collection.
    /// </summary>
    public static IEnumerable<T> Near<T>(BaseLiteDB.ILiteCollection<T> collection, Expression<Func<T, GeoPoint3D>> accessor, GeoPoint3D center, double radius, int? limit = null)
    {
        var liteCollection = GetLiteCollection(collection);
        var descriptor = GetDescriptor(liteCollection, Cartesian3DSuggestion);
        EnsureDimensions(descriptor, 3);

        if (!string.Equals(descriptor.EngineName, Cartesian3DEngine.EngineName, StringComparison.Ordinal))
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for '{descriptor.EngineName}' and cannot execute three-dimensional Near queries.");
        }

        var engine = EnsureEngine(descriptor);
        var plan = SpatialCartesian3D.Near(descriptor, center, radius);
        var predicate = BuildPredicate(descriptor, plan);
        var getter = accessor.Compile();
        var source = ExecuteQuery(liteCollection, predicate);
        var tolerance = descriptor.Options.DistanceTolerance;

        var results = new List<(T Item, double Distance)>();
        foreach (var item in source)
        {
            var point = getter(item);
            var distance = engine.Distance.Distance(point, center);
            if (distance <= radius + tolerance)
            {
                results.Add((item, distance));
            }
        }

        results.Sort((left, right) => left.Distance.CompareTo(right.Distance));

        return limit.HasValue
            ? results.Take(limit.Value).Select(tuple => tuple.Item).ToList()
            : results.Select(tuple => tuple.Item).ToList();
    }

    /// <summary>
    /// Returns all entries inside the provided two-dimensional bounding box.
    /// </summary>
    public static IEnumerable<T> WithinBoundingBox<T>(BaseLiteDB.ILiteCollection<T> collection, Expression<Func<T, GeoPoint>> accessor, BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Bounding box must describe two dimensions for GeoPoint queries.", nameof(bounds));
        }

        var liteCollection = GetLiteCollection(collection);
        var descriptor = GetDescriptor(liteCollection, GeographicSuggestion, Cartesian2DSuggestion);
        EnsureDimensions(descriptor, 2);
        descriptor.EnsureCompatible(bounds);

        var plan = descriptor.EngineName switch
        {
            GeographicEngine.EngineName => SpatialGeographic.WithinBoundingBox(descriptor, bounds),
            Cartesian2DEngine.EngineName => SpatialCartesian2D.WithinBoundingBox(descriptor, bounds),
            _ => throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for '{descriptor.EngineName}' which does not support two-dimensional bounding boxes.")
        };

        var predicate = BuildPredicate(descriptor, plan);
        var getter = accessor.Compile();
        var source = ExecuteQuery(liteCollection, predicate);

        var minX = bounds.MinX;
        var maxX = bounds.MaxX;
        var minY = bounds.MinY;
        var maxY = bounds.MaxY;

        return source
            .Where(item =>
            {
                var point = getter(item);
                return point.Longitude >= minX && point.Longitude <= maxX && point.Latitude >= minY && point.Latitude <= maxY;
            })
            .ToList();
    }

    /// <summary>
    /// Returns all entries inside the provided three-dimensional bounding box.
    /// </summary>
    public static IEnumerable<T> WithinBoundingBox<T>(BaseLiteDB.ILiteCollection<T> collection, Expression<Func<T, GeoPoint3D>> accessor, BoundingBox bounds)
    {
        if (bounds.Dimensions != 3)
        {
            throw new ArgumentException("Bounding box must describe three dimensions for GeoPoint3D queries.", nameof(bounds));
        }

        var liteCollection = GetLiteCollection(collection);
        var descriptor = GetDescriptor(liteCollection, Cartesian3DSuggestion);
        EnsureDimensions(descriptor, 3);
        descriptor.EnsureCompatible(bounds);

        if (!string.Equals(descriptor.EngineName, Cartesian3DEngine.EngineName, StringComparison.Ordinal))
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for '{descriptor.EngineName}' and cannot execute three-dimensional bounding box queries.");
        }

        var plan = SpatialCartesian3D.WithinBoundingBox(descriptor, bounds);
        var predicate = BuildPredicate(descriptor, plan);
        var getter = accessor.Compile();
        var source = ExecuteQuery(liteCollection, predicate);

        var values = bounds.GetValues();
        var minX = values[0];
        var minY = values[1];
        var minZ = values[2];
        var maxX = values[3];
        var maxY = values[4];
        var maxZ = values[5];

        return source
            .Where(item =>
            {
                var point = getter(item);
                return point.X >= minX && point.X <= maxX
                    && point.Y >= minY && point.Y <= maxY
                    && point.Z >= minZ && point.Z <= maxZ;
            })
            .ToList();
    }

    private static SpatialCollectionDescriptor EnsurePointIndexInternal<T, TPoint>(BaseLiteDB.ILiteCollection<T> collection, Expression<Func<T, TPoint>> accessor, SpatialIndexOptions? options, int expectedDimensions)
    {
        var liteCollection = GetLiteCollection(collection);
        var fieldName = ResolveGeometryField(liteCollection, accessor);
        var metadata = GetMetadataStore(liteCollection);
        var bsonCollection = GetBsonCollection(liteCollection);
        var descriptor = expectedDimensions == 3
            ? metadata.GetRequiredDescriptor(liteCollection.Name, Cartesian3DSuggestion)
            : metadata.GetRequiredDescriptor(liteCollection.Name, GeographicSuggestion);

        EnsureDimensions(descriptor, expectedDimensions);

        var geometryGetter = accessor.Compile();
        var effectiveOptions = options ?? descriptor.Options;
        SpatialCollectionDescriptor result;

        switch (descriptor.EngineName)
        {
            case GeographicEngine.EngineName when expectedDimensions == 2:
                var distanceMode = descriptor.Settings.DistanceMode ?? GeographicDistanceMode.Haversine;
                result = SpatialGeographic.EnsurePointIndex(metadata, bsonCollection, fieldName, effectiveOptions, distanceMode);
                break;
            case Cartesian2DEngine.EngineName when expectedDimensions == 2:
                if (descriptor.Settings.Domain is not { } domain2D)
                {
                    throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is missing domain metadata. Call Spatial.UseCartesian2D before ensuring indexes.");
                }

                result = SpatialCartesian2D.EnsurePointIndex(metadata, bsonCollection, fieldName, domain2D, effectiveOptions);
                break;
            case Cartesian3DEngine.EngineName when expectedDimensions == 3:
                if (descriptor.Settings.Domain is not { } domain3D)
                {
                    throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is missing domain metadata. Call Spatial.UseCartesian3D before ensuring indexes.");
                }

                result = SpatialCartesian3D.EnsurePointIndex(metadata, bsonCollection, fieldName, domain3D, effectiveOptions);
                break;
            default:
                throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for engine '{descriptor.EngineName}' which does not support {expectedDimensions}D point indexes.");
        }

        var engine = EnsureEngine(result);
        RegisterComputedMembers(liteCollection, geometryGetter, result, engine, expectedDimensions);
        EnsureIndexOnDollarIdx(bsonCollection, result.Options.IndexFieldName);
        return result;
    }

    private static BaseLiteDB.LiteCollection<T> GetLiteCollection<T>(BaseLiteDB.ILiteCollection<T> collection)
    {
        if (collection is BaseLiteDB.LiteCollection<T> liteCollection)
        {
            return liteCollection;
        }

        throw new NotSupportedException("Spatial helpers require LiteCollection<T> instances.");
    }

    private static SpatialMetadataStore GetMetadataStore<T>(BaseLiteDB.LiteCollection<T> collection)
    {
        var engine = GetEngine(collection);
        var key = (Engine: (object)engine, collection.Name);

        return MetadataStores.GetOrAdd(key, _ => new SpatialMetadataStore(CreateDatabase(collection)));
    }

    private static BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> GetBsonCollection<T>(BaseLiteDB.LiteCollection<T> collection)
    {
        var database = CreateDatabase(collection);
        return database.GetCollection(collection.Name);
    }

    private static BaseLiteDB.LiteDatabase CreateDatabase<T>(BaseLiteDB.LiteCollection<T> collection)
    {
        var engine = GetEngine(collection);
        var mapper = GetMapper(collection) ?? BaseLiteDB.BsonMapper.Global;
        return new BaseLiteDB.LiteDatabase(engine, mapper, disposeOnClose: false);
    }

    private static BaseEngine.ILiteEngine GetEngine<T>(BaseLiteDB.LiteCollection<T> collection)
    {
        var field = typeof(BaseLiteDB.LiteCollection<T>).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            throw new SpatialMetadataException("Unable to access LiteCollection engine instance.");
        }

        var value = field.GetValue(collection);
        return value as BaseEngine.ILiteEngine ?? throw new SpatialMetadataException("LiteCollection did not expose an engine instance.");
    }

    private static BaseLiteDB.BsonMapper? GetMapper<T>(BaseLiteDB.LiteCollection<T> collection)
    {
        var field = typeof(BaseLiteDB.LiteCollection<T>).GetField("_mapper", BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(collection) as BaseLiteDB.BsonMapper;
    }

    private static string ResolveGeometryField<T, TPoint>(BaseLiteDB.LiteCollection<T> collection, Expression<Func<T, TPoint>> accessor)
    {
        if (accessor == null)
        {
            throw new ArgumentNullException(nameof(accessor));
        }

        var mapper = collection.EntityMapper;
        if (mapper == null)
        {
            throw new SpatialMetadataException("Strongly typed collections are required to resolve geometry members.");
        }

        var member = mapper.GetMember(accessor.Body);
        if (member == null)
        {
            throw new SpatialMetadataException($"Unable to resolve geometry member for expression '{accessor}'. Ensure the property is mapped by BsonMapper.");
        }

        return member.FieldName;
    }

    private static SpatialCollectionDescriptor GetDescriptor<T>(BaseLiteDB.LiteCollection<T> collection, params string[] suggestions)
    {
        var metadata = GetMetadataStore(collection);
        var suggestion = suggestions.Length switch
        {
            0 => null,
            1 => suggestions[0],
            2 => string.Concat(suggestions[0], " or ", suggestions[1]),
            _ => string.Join(" or ", suggestions)
        };

        return metadata.GetRequiredDescriptor(collection.Name, suggestion);
    }

    private static void EnsureDimensions(SpatialCollectionDescriptor descriptor, int expected)
    {
        if (descriptor.Dimensions != expected)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for {descriptor.Dimensions}D geometry but the operation expects {expected}D.");
        }
    }

    private static ISpatialEngine EnsureEngine(SpatialCollectionDescriptor descriptor)
    {
        if (descriptor.TryGetEngine(out var engine))
        {
            return engine;
        }

        return CreateEngine(descriptor);
    }

    private static ISpatialEngine CreateEngine(SpatialCollectionDescriptor descriptor)
    {
        return descriptor.EngineName switch
        {
            GeographicEngine.EngineName => new GeographicEngine(descriptor.GeometryFieldName, descriptor.Options, descriptor.Settings.DistanceMode ?? GeographicDistanceMode.Haversine),
            Cartesian2DEngine.EngineName when descriptor.Settings.Domain is { } domain2D => new Cartesian2DEngine(descriptor.GeometryFieldName, domain2D, descriptor.Options),
            Cartesian3DEngine.EngineName when descriptor.Settings.Domain is { } domain3D => new Cartesian3DEngine(descriptor.GeometryFieldName, domain3D, descriptor.Options),
            Cartesian2DEngine.EngineName => throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is missing Cartesian domain metadata. Call Spatial.UseCartesian2D before querying."),
            Cartesian3DEngine.EngineName => throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is missing Cartesian domain metadata. Call Spatial.UseCartesian3D before querying."),
            _ => throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for unknown engine '{descriptor.EngineName}'.")
        };
    }

    private static BaseLiteDB.BsonExpression? BuildPredicate(SpatialCollectionDescriptor descriptor, ISpatialQueryPlan plan)
    {
        var indexPredicate = BuildIndexPredicate(descriptor.Options.IndexFieldName, plan.IndexRanges);
        var boundingPredicate = BuildBoundingPredicate(descriptor.Options.BoundingBoxFieldName, plan.CoveringBounds);
        return CombinePredicates(indexPredicate, boundingPredicate);
    }

    private static BaseLiteDB.BsonExpression? BuildIndexPredicate(string indexFieldName, IReadOnlyList<SpatialIndexRange> ranges)
    {
        if (ranges == null || ranges.Count == 0)
        {
            return null;
        }

        var expressions = new BaseLiteDB.BsonExpression[ranges.Count];
        for (var i = 0; i < ranges.Count; i++)
        {
            var range = ranges[i];
            var start = ToBsonValue(range.Start);
            var end = ToBsonValue(range.End);
            expressions[i] = BaseLiteDB.Query.Between("$." + indexFieldName, start, end);
        }

        return expressions.Length == 1
            ? expressions[0]
            : BaseLiteDB.Query.Or(expressions);
    }

    private static BaseLiteDB.BsonExpression? BuildBoundingPredicate(string boundingFieldName, BoundingBox? bounds)
    {
        if (!bounds.HasValue)
        {
            return null;
        }

        var values = bounds.Value.GetValues();
        var dimension = bounds.Value.Dimensions;
        var conditions = new List<string>(dimension * 2 + 1)
        {
            string.Format(CultureInfo.InvariantCulture, "$.{0} != null", boundingFieldName)
        };

        var parameters = new List<BaseLiteDB.BsonValue>(dimension * 2);
        for (var axis = 0; axis < dimension; axis++)
        {
            var minIndex = axis;
            var maxIndex = axis + dimension;
            conditions.Add(string.Format(CultureInfo.InvariantCulture, "$.{0}[{1}] <= @{2}", boundingFieldName, minIndex, parameters.Count));
            parameters.Add(new BaseLiteDB.BsonValue(values[maxIndex]));
            conditions.Add(string.Format(CultureInfo.InvariantCulture, "$.{0}[{1}] >= @{2}", boundingFieldName, maxIndex, parameters.Count));
            parameters.Add(new BaseLiteDB.BsonValue(values[minIndex]));
        }

        var predicate = string.Join(" AND ", conditions);
        return BaseLiteDB.BsonExpression.Create(predicate, parameters.ToArray());
    }

    private static BaseLiteDB.BsonExpression? CombinePredicates(BaseLiteDB.BsonExpression? left, BaseLiteDB.BsonExpression? right)
    {
        if (left == null)
        {
            return right;
        }

        if (right == null)
        {
            return left;
        }

        var parameters = new BaseLiteDB.BsonDocument();
        void CopyParameters(BaseLiteDB.BsonExpression expression)
        {
            if (expression.Parameters == null)
            {
                return;
            }

            foreach (var parameter in expression.Parameters)
            {
                parameters[parameter.Key] = parameter.Value;
            }
        }

        CopyParameters(left);
        CopyParameters(right);

        var source = string.Format(CultureInfo.InvariantCulture, "({0}) AND ({1})", left.Source, right.Source);
        return BaseLiteDB.BsonExpression.Create(source, parameters);
    }

    private static IEnumerable<T> ExecuteQuery<T>(BaseLiteDB.LiteCollection<T> collection, BaseLiteDB.BsonExpression? predicate)
    {
        if (predicate == null)
        {
            return collection.FindAll();
        }

        return collection.Find(predicate);
    }

    private static void EnsureIndexOnDollarIdx(BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection, string fieldName)
    {
        var expression = BaseLiteDB.BsonExpression.Create("$." + fieldName);
        collection.EnsureIndex(fieldName, expression);
    }

    private static void RegisterComputedMembers<T, TPoint>(BaseLiteDB.LiteCollection<T> collection, Func<T, TPoint> getter, SpatialCollectionDescriptor descriptor, ISpatialEngine engine, int expectedDimensions)
    {
        if (engine.Mapper == null)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is associated with engine '{descriptor.EngineName}' which does not expose a mapper.");
        }

        if (expectedDimensions == 2 && getter is Func<T, GeoPoint> getter2D)
        {
            RegisterComputedMembers2D(collection, getter2D, descriptor, engine.Mapper);
        }
        else if (expectedDimensions == 3 && getter is Func<T, GeoPoint3D> getter3D)
        {
            RegisterComputedMembers3D(collection, getter3D, descriptor, engine.Mapper);
        }
        else
        {
            throw new SpatialMetadataException($"Geometry accessor for collection '{descriptor.CollectionName}' did not match the expected dimensionality.");
        }
    }

    private static void RegisterComputedMembers2D<T>(BaseLiteDB.LiteCollection<T> collection, Func<T, GeoPoint> getter, SpatialCollectionDescriptor descriptor, ISpatialMapper mapper)
    {
        EnsureComputedMember(collection, descriptor.Options.IndexFieldName, typeof(BaseLiteDB.BsonValue), entity =>
        {
            var point = getter(entity);
            var index = mapper.Encode(point);
            return CreateIndexBsonValue(index);
        });

        EnsureComputedMember(collection, descriptor.Options.BoundingBoxFieldName, typeof(double[]), entity =>
        {
            var point = getter(entity);
            var box = mapper.GetBoundingBox(point);
            return box.ToArray();
        });
    }

    private static void RegisterComputedMembers3D<T>(BaseLiteDB.LiteCollection<T> collection, Func<T, GeoPoint3D> getter, SpatialCollectionDescriptor descriptor, ISpatialMapper mapper)
    {
        EnsureComputedMember(collection, descriptor.Options.IndexFieldName, typeof(BaseLiteDB.BsonValue), entity =>
        {
            var point = getter(entity);
            var index = mapper.Encode(point);
            return CreateIndexBsonValue(index);
        });

        EnsureComputedMember(collection, descriptor.Options.BoundingBoxFieldName, typeof(double[]), entity =>
        {
            var point = getter(entity);
            var box = mapper.GetBoundingBox(point);
            return box.ToArray();
        });
    }

    private static void EnsureComputedMember<T>(BaseLiteDB.LiteCollection<T> collection, string fieldName, Type dataType, Func<T, object?> getter)
    {
        var mapper = collection.EntityMapper ?? throw new SpatialMetadataException("Strongly typed collections are required to resolve geometry members.");
        mapper.WaitForInitialization();

        var member = mapper.Members.FirstOrDefault(x => string.Equals(x.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
        if (member == null)
        {
            var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MemberInfo? memberInfo = typeof(T).GetProperty(fieldName, bindingFlags);
            if (memberInfo == null)
            {
                memberInfo = typeof(T).GetField(fieldName, bindingFlags);
            }

            member = new BaseLiteDB.MemberMapper
            {
                FieldName = fieldName,
                MemberName = memberInfo?.Name ?? fieldName,
                DataType = dataType,
                UnderlyingType = dataType,
                Getter = obj => getter((T)obj),
                Setter = null,
                IsEnumerable = EnumerableType.IsAssignableFrom(dataType) && dataType != typeof(string)
            };

            mapper.Members.Add(member);
        }
        else
        {
            member.Getter = obj => getter((T)obj);
            member.DataType = dataType;
            member.UnderlyingType = dataType;
            member.IsEnumerable = EnumerableType.IsAssignableFrom(dataType) && dataType != typeof(string);
        }
    }

    private static BaseLiteDB.BsonValue CreateIndexBsonValue(ulong index)
    {
        if (index <= long.MaxValue)
        {
            return new BaseLiteDB.BsonValue((long)index);
        }

        return new BaseLiteDB.BsonValue((decimal)index);
    }

    private static BaseLiteDB.BsonValue ToBsonValue(ulong value)
    {
        if (value <= long.MaxValue)
        {
            return new BaseLiteDB.BsonValue((long)value);
        }

        return new BaseLiteDB.BsonValue((decimal)value);
    }
}
