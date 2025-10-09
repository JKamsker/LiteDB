extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Serializes and deserializes geometry objects using the GeoJSON specification.
/// </summary>
public static class GeoJsonSerializer
{
    /// <summary>
    /// Converts a geometry object into a GeoJSON <see cref="BaseLiteDB.BsonValue"/> representation.
    /// </summary>
    /// <param name="geometry">The geometry instance to serialize.</param>
    /// <returns>A <see cref="BaseLiteDB.BsonValue"/> describing the geometry.</returns>
    public static BaseLiteDB.BsonValue ToBson(object? geometry)
    {
        return geometry switch
        {
            null => BaseLiteDB.BsonValue.Null,
            GeoPoint point => CreateDocument("Point", BuildPosition(point)),
            GeoLineString line => CreateDocument("LineString", BuildLineStringCoordinates(line)),
            GeoPolygon polygon => CreateDocument("Polygon", BuildPolygonCoordinates(polygon)),
            _ => throw new ArgumentException($"Unsupported geometry type '{geometry.GetType().FullName}'.", nameof(geometry))
        };
    }

    /// <summary>
    /// Deserializes the provided GeoJSON <see cref="BaseLiteDB.BsonValue"/> into a CLR geometry.
    /// </summary>
    /// <param name="value">The GeoJSON representation.</param>
    /// <returns>The CLR geometry instance or <c>null</c> when <paramref name="value"/> is <c>null</c>.</returns>
    public static object? FromBson(BaseLiteDB.BsonValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        if (!value.IsDocument)
        {
            throw new ArgumentException("GeoJSON payload must be a document.", nameof(value));
        }

        var document = value.AsDocument;
        if (!document.TryGetValue("type", out var typeValue) || !typeValue.IsString)
        {
            throw new ArgumentException("GeoJSON document must contain a string 'type' property.", nameof(value));
        }

        var type = typeValue.AsString;
        var coordinates = EnsureArray(document, "coordinates");

        return type switch
        {
            "Point" => ParsePoint(coordinates),
            "LineString" => ParseLineString(coordinates),
            "Polygon" => ParsePolygon(coordinates),
            _ => throw new ArgumentException($"Unsupported GeoJSON geometry type '{type}'.", nameof(value))
        };
    }

    /// <summary>
    /// Deserializes the GeoJSON payload into a strongly typed geometry instance.
    /// </summary>
    /// <typeparam name="T">The expected geometry type.</typeparam>
    /// <param name="value">The GeoJSON representation.</param>
    /// <returns>The typed geometry instance.</returns>
    public static T FromBson<T>(BaseLiteDB.BsonValue value)
    {
        var geometry = FromBson(value);
        if (geometry is null)
        {
            if (default(T) is null)
            {
                return default!;
            }

            throw new ArgumentException("GeoJSON payload is null but the requested type is not nullable.", nameof(value));
        }

        if (geometry is T typed)
        {
            return typed;
        }

        throw new ArgumentException($"GeoJSON payload describes '{geometry.GetType().Name}' instead of '{typeof(T).Name}'.", nameof(value));
    }

    private static BaseLiteDB.BsonDocument CreateDocument(string type, BaseLiteDB.BsonValue coordinates)
    {
        return new BaseLiteDB.BsonDocument
        {
            ["type"] = type,
            ["coordinates"] = coordinates
        };
    }

    private static BaseLiteDB.BsonValue BuildPosition(GeoPoint point)
    {
        return new BaseLiteDB.BsonArray { point.Longitude, point.Latitude };
    }

    private static BaseLiteDB.BsonValue BuildLineStringCoordinates(GeoLineString line)
    {
        var array = new BaseLiteDB.BsonArray();
        foreach (var point in line.Points)
        {
            array.Add(BuildPosition(point));
        }

        return array;
    }

    private static BaseLiteDB.BsonValue BuildPolygonCoordinates(GeoPolygon polygon)
    {
        var rings = new BaseLiteDB.BsonArray
        {
            BuildLinearRing(polygon.Outer)
        };

        foreach (var hole in polygon.Holes)
        {
            rings.Add(BuildLinearRing(hole));
        }

        return rings;
    }

    private static BaseLiteDB.BsonArray BuildLinearRing(IReadOnlyList<GeoPoint> points)
    {
        var ring = new BaseLiteDB.BsonArray();
        foreach (var point in points)
        {
            ring.Add(BuildPosition(point));
        }

        return ring;
    }

    private static GeoPoint ParsePoint(BaseLiteDB.BsonArray coordinates)
    {
        if (coordinates.Count < 2)
        {
            throw new ArgumentException("GeoJSON point coordinates must contain [longitude, latitude].", nameof(coordinates));
        }

        return new GeoPoint(coordinates[0].AsDouble, coordinates[1].AsDouble);
    }

    private static GeoLineString ParseLineString(BaseLiteDB.BsonArray coordinates)
    {
        var points = new List<GeoPoint>(coordinates.Count);
        foreach (var value in coordinates)
        {
            if (!value.IsArray)
            {
                throw new ArgumentException("GeoJSON LineString coordinates must be arrays of positions.", nameof(coordinates));
            }

            points.Add(ParsePoint(value.AsArray));
        }

        return new GeoLineString(points);
    }

    private static GeoPolygon ParsePolygon(BaseLiteDB.BsonArray coordinates)
    {
        if (coordinates.Count == 0)
        {
            throw new ArgumentException("GeoJSON Polygon requires at least one linear ring.", nameof(coordinates));
        }

        var outerRing = ParseLinearRing(coordinates[0]);
        var holes = new List<IReadOnlyList<GeoPoint>>();

        for (var i = 1; i < coordinates.Count; i++)
        {
            holes.Add(ParseLinearRing(coordinates[i]));
        }

        return new GeoPolygon(outerRing, holes);
    }

    private static IReadOnlyList<GeoPoint> ParseLinearRing(BaseLiteDB.BsonValue value)
    {
        if (!value.IsArray)
        {
            throw new ArgumentException("GeoJSON linear rings must be arrays of positions.", nameof(value));
        }

        var coordinates = value.AsArray;
        var points = new List<GeoPoint>(coordinates.Count);
        foreach (var position in coordinates)
        {
            if (!position.IsArray)
            {
                throw new ArgumentException("GeoJSON positions must be arrays of [longitude, latitude].", nameof(value));
            }

            points.Add(ParsePoint(position.AsArray));
        }

        return points;
    }

    private static BaseLiteDB.BsonArray EnsureArray(BaseLiteDB.BsonDocument document, string key)
    {
        if (!document.TryGetValue(key, out var value) || !value.IsArray)
        {
            throw new ArgumentException($"GeoJSON document must contain an array '{key}'.", key);
        }

        return value.AsArray;
    }
}
