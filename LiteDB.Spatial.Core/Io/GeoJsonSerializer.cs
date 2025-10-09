#nullable enable

extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using System.Linq;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Provides helpers for converting spatial geometries to and from GeoJSON-compatible <see cref="BaseLiteDB.BsonValue"/> payloads.
/// </summary>
public static class GeoJsonSerializer
{
    /// <summary>
    /// Serializes the provided geometry into a GeoJSON <see cref="BaseLiteDB.BsonValue"/> payload.
    /// </summary>
    /// <param name="geometry">The geometry to serialize. May be <c>null</c>.</param>
    /// <returns>A GeoJSON document or <see cref="BaseLiteDB.BsonValue.Null"/> when <paramref name="geometry"/> is <c>null</c>.</returns>
    public static BaseLiteDB.BsonValue Serialize(object? geometry)
    {
        return geometry switch
        {
            null => BaseLiteDB.BsonValue.Null,
            GeoPoint point => Serialize(point),
            GeoLineString line => Serialize(line),
            GeoPolygon polygon => Serialize(polygon),
            _ => throw new ArgumentException($"Unsupported geometry type '{geometry.GetType().FullName}'.", nameof(geometry))
        };
    }

    /// <summary>
    /// Serializes a geographic point into GeoJSON.
    /// </summary>
    public static BaseLiteDB.BsonValue Serialize(GeoPoint point)
    {
        return new BaseLiteDB.BsonDocument
        {
            ["type"] = "Point",
            ["coordinates"] = new BaseLiteDB.BsonArray { point.Longitude, point.Latitude }
        };
    }

    /// <summary>
    /// Serializes a line string into GeoJSON.
    /// </summary>
    public static BaseLiteDB.BsonValue Serialize(GeoLineString line)
    {
        if (line == null)
        {
            throw new ArgumentNullException(nameof(line));
        }

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

    /// <summary>
    /// Serializes a polygon into GeoJSON.
    /// </summary>
    public static BaseLiteDB.BsonValue Serialize(GeoPolygon polygon)
    {
        if (polygon == null)
        {
            throw new ArgumentNullException(nameof(polygon));
        }

        var rings = new BaseLiteDB.BsonArray
        {
            CreateRing(polygon.Outer)
        };

        foreach (var hole in polygon.Holes)
        {
            rings.Add(CreateRing(hole));
        }

        return new BaseLiteDB.BsonDocument
        {
            ["type"] = "Polygon",
            ["coordinates"] = rings
        };
    }

    /// <summary>
    /// Deserializes a GeoJSON payload into a geometry instance.
    /// </summary>
    /// <param name="value">The serialized GeoJSON value.</param>
    /// <returns>The geometry instance, or <c>null</c> when the payload represents <c>null</c>.</returns>
    public static object? Deserialize(BaseLiteDB.BsonValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        var document = EnsureDocument(value);
        var type = ReadType(document);

        return type switch
        {
            "Point" => DeserializePoint(document),
            "LineString" => DeserializeLineString(document),
            "Polygon" => DeserializePolygon(document),
            _ => throw new FormatException($"Unsupported GeoJSON geometry type '{type}'.")
        };
    }

    /// <summary>
    /// Deserializes the provided GeoJSON payload into a <see cref="GeoPoint"/>.
    /// </summary>
    public static GeoPoint DeserializePoint(BaseLiteDB.BsonValue value)
    {
        var document = EnsureDocument(value, "Point");
        var coordinates = EnsureArray(document, "coordinates");
        if (coordinates.Count < 2)
        {
            throw new FormatException("GeoJSON point coordinates must contain longitude and latitude.");
        }

        var longitude = coordinates[0].AsDouble;
        var latitude = coordinates[1].AsDouble;
        return new GeoPoint(longitude, latitude);
    }

    /// <summary>
    /// Deserializes the provided GeoJSON payload into a <see cref="GeoLineString"/>.
    /// </summary>
    public static GeoLineString DeserializeLineString(BaseLiteDB.BsonValue value)
    {
        var document = EnsureDocument(value, "LineString");
        var coordinates = EnsureArray(document, "coordinates");
        if (coordinates.Count < 2)
        {
            throw new FormatException("LineString requires at least two coordinate positions.");
        }

        var points = new List<GeoPoint>(coordinates.Count);
        foreach (var entry in coordinates)
        {
            points.Add(ReadPosition(entry));
        }

        return new GeoLineString(points);
    }

    /// <summary>
    /// Deserializes the provided GeoJSON payload into a <see cref="GeoPolygon"/>.
    /// </summary>
    public static GeoPolygon DeserializePolygon(BaseLiteDB.BsonValue value)
    {
        var document = EnsureDocument(value, "Polygon");
        var ringsArray = EnsureArray(document, "coordinates");
        if (ringsArray.Count == 0)
        {
            throw new FormatException("Polygon requires at least one linear ring.");
        }

        var rings = ringsArray.Select(ReadRing).ToList();
        var outer = rings[0];
        var holes = rings.Count > 1 ? rings.Skip(1).Select(r => (IReadOnlyList<GeoPoint>)r).ToList() : null;

        return new GeoPolygon(outer, holes);
    }

    private static BaseLiteDB.BsonDocument EnsureDocument(BaseLiteDB.BsonValue value, string? expectedType = null)
    {
        if (!value.IsDocument)
        {
            throw new FormatException("GeoJSON payload must be stored as a document with type and coordinates.");
        }

        var document = value.AsDocument;
        var type = ReadType(document);

        if (expectedType != null && !string.Equals(type, expectedType, StringComparison.Ordinal))
        {
            throw new FormatException($"GeoJSON payload describes '{type}' but '{expectedType}' was expected.");
        }

        return document;
    }

    private static string ReadType(BaseLiteDB.BsonDocument document)
    {
        if (!document.TryGetValue("type", out var typeValue) || !typeValue.IsString)
        {
            throw new FormatException("GeoJSON payload must include a string 'type' property.");
        }

        return typeValue.AsString;
    }

    private static BaseLiteDB.BsonArray EnsureArray(BaseLiteDB.BsonDocument document, string key)
    {
        if (!document.TryGetValue(key, out var value) || !value.IsArray)
        {
            throw new FormatException($"GeoJSON '{key}' property must be an array.");
        }

        return value.AsArray;
    }

    private static BaseLiteDB.BsonArray CreateRing(IReadOnlyList<GeoPoint> points)
    {
        var array = new BaseLiteDB.BsonArray();
        foreach (var point in points)
        {
            array.Add(new BaseLiteDB.BsonArray { point.Longitude, point.Latitude });
        }

        return array;
    }

    private static GeoPoint ReadPosition(BaseLiteDB.BsonValue value)
    {
        if (!value.IsArray)
        {
            throw new FormatException("Coordinate position must be expressed as an array.");
        }

        var array = value.AsArray;
        if (array.Count < 2)
        {
            throw new FormatException("Coordinate position must contain longitude and latitude.");
        }

        return new GeoPoint(array[0].AsDouble, array[1].AsDouble);
    }

    private static List<GeoPoint> ReadRing(BaseLiteDB.BsonValue value)
    {
        if (!value.IsArray)
        {
            throw new FormatException("Polygon rings must be arrays of coordinate positions.");
        }

        var array = value.AsArray;
        if (array.Count < 4)
        {
            throw new FormatException("Polygon rings must contain at least four positions including closure.");
        }

        var points = new List<GeoPoint>(array.Count);
        foreach (var entry in array)
        {
            points.Add(ReadPosition(entry));
        }

        return points;
    }
}

