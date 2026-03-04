extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Provides helpers for serializing and deserializing simple GeoJSON geometries.
/// </summary>
public static class GeoJsonSerializer
{
    /// <summary>
    /// Serializes a geometry instance into its GeoJSON representation.
    /// </summary>
    /// <param name="geometry">The geometry to serialize.</param>
    /// <returns>A <see cref="BaseLiteDB.BsonValue"/> describing the geometry.</returns>
    public static BaseLiteDB.BsonValue ToBson(object? geometry)
    {
        switch (geometry)
        {
            case null:
                return BaseLiteDB.BsonValue.Null;
            case GeoPoint point:
                return SerializePoint(point);
            case GeoLineString line:
                return SerializeLineString(line);
            case GeoPolygon polygon:
                return SerializePolygon(polygon);
            default:
                throw new ArgumentException($"Unsupported geometry type '{geometry.GetType().Name}'.", nameof(geometry));
        }
    }

    /// <summary>
    /// Deserializes a GeoJSON payload into a geometry instance.
    /// </summary>
    /// <param name="value">The GeoJSON payload.</param>
    /// <returns>The geometry described by the payload.</returns>
    public static object? FromBson(BaseLiteDB.BsonValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        if (!value.IsDocument)
        {
            throw new FormatException("GeoJSON payload must be stored as a document.");
        }

        var document = value.AsDocument;
        if (!document.TryGetValue("type", out var typeValue) || !typeValue.IsString)
        {
            throw new FormatException("GeoJSON document must include a string 'type' property.");
        }

        if (!document.TryGetValue("coordinates", out var coordinatesValue) || !coordinatesValue.IsArray)
        {
            throw new FormatException("GeoJSON document must include a 'coordinates' array.");
        }

        var coordinates = coordinatesValue.AsArray;
        return typeValue.AsString switch
        {
            "Point" => DeserializePoint(coordinates),
            "LineString" => DeserializeLineString(coordinates),
            "Polygon" => DeserializePolygon(coordinates),
            var type => throw new FormatException($"Unsupported GeoJSON geometry type '{type}'.")
        };
    }

    /// <summary>
    /// Deserializes a GeoJSON payload to the requested geometry type.
    /// </summary>
    /// <typeparam name="T">The expected geometry type.</typeparam>
    /// <param name="value">The GeoJSON payload.</param>
    /// <returns>The geometry instance.</returns>
    public static T FromBson<T>(BaseLiteDB.BsonValue value)
    {
        var geometry = FromBson(value);

        if (geometry is null)
        {
            if (default(T) is null)
            {
                return default!;
            }

            throw new FormatException($"GeoJSON payload is null but '{typeof(T).Name}' was requested.");
        }

        if (geometry is T typed)
        {
            return typed;
        }

        throw new FormatException($"GeoJSON payload describes a '{geometry.GetType().Name}' but '{typeof(T).Name}' was requested.");
    }

    private static BaseLiteDB.BsonValue SerializePoint(GeoPoint point)
    {
        return new BaseLiteDB.BsonDocument
        {
            ["type"] = "Point",
            ["coordinates"] = new BaseLiteDB.BsonArray { point.Longitude, point.Latitude }
        };
    }

    private static BaseLiteDB.BsonValue SerializeLineString(GeoLineString line)
    {
        var coordinates = new BaseLiteDB.BsonArray();
        foreach (var point in line.Points)
        {
            coordinates.Add(new BaseLiteDB.BsonArray { point.Longitude, point.Latitude });
        }

        return new BaseLiteDB.BsonDocument
        {
            ["type"] = "LineString",
            ["coordinates"] = coordinates
        };
    }

    private static BaseLiteDB.BsonValue SerializePolygon(GeoPolygon polygon)
    {
        var rings = new BaseLiteDB.BsonArray
        {
            ToRing(polygon.Outer)
        };

        foreach (var hole in polygon.Holes)
        {
            rings.Add(ToRing(hole));
        }

        return new BaseLiteDB.BsonDocument
        {
            ["type"] = "Polygon",
            ["coordinates"] = rings
        };
    }

    private static BaseLiteDB.BsonArray ToRing(IEnumerable<GeoPoint> points)
    {
        var ring = new BaseLiteDB.BsonArray();
        foreach (var point in points)
        {
            ring.Add(new BaseLiteDB.BsonArray { point.Longitude, point.Latitude });
        }

        return ring;
    }

    private static GeoPoint DeserializePoint(BaseLiteDB.BsonArray coordinates)
    {
        if (coordinates.Count < 2)
        {
            throw new FormatException("GeoJSON points require at least two coordinate values.");
        }

        return new GeoPoint(coordinates[0].AsDouble, coordinates[1].AsDouble);
    }

    private static GeoLineString DeserializeLineString(BaseLiteDB.BsonArray coordinates)
    {
        if (coordinates.Count < 2)
        {
            throw new FormatException("GeoJSON LineString requires at least two positions.");
        }

        var points = new GeoPoint[coordinates.Count];
        for (var i = 0; i < coordinates.Count; i++)
        {
            if (!coordinates[i].IsArray)
            {
                throw new FormatException("GeoJSON LineString coordinates must be arrays of positions.");
            }

            points[i] = DeserializePoint(coordinates[i].AsArray);
        }

        return new GeoLineString(points);
    }

    private static GeoPolygon DeserializePolygon(BaseLiteDB.BsonArray coordinates)
    {
        if (coordinates.Count == 0)
        {
            throw new FormatException("GeoJSON Polygon requires at least one linear ring.");
        }

        var outer = DeserializeRing(coordinates[0]);
        var holes = new List<IReadOnlyList<GeoPoint>>();

        for (var i = 1; i < coordinates.Count; i++)
        {
            holes.Add(DeserializeRing(coordinates[i]));
        }

        return new GeoPolygon(outer, holes);
    }

    private static IReadOnlyList<GeoPoint> DeserializeRing(BaseLiteDB.BsonValue value)
    {
        if (!value.IsArray)
        {
            throw new FormatException("GeoJSON polygon rings must be arrays of positions.");
        }

        var array = value.AsArray;
        if (array.Count < 4)
        {
            throw new FormatException("GeoJSON polygon rings must contain at least four positions.");
        }

        var points = new GeoPoint[array.Count];
        for (var i = 0; i < array.Count; i++)
        {
            if (!array[i].IsArray)
            {
                throw new FormatException("GeoJSON polygon ring coordinates must be arrays of positions.");
            }

            points[i] = DeserializePoint(array[i].AsArray);
        }

        return new ReadOnlyCollection<GeoPoint>(points);
    }
}
