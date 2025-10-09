extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Serializes and deserializes GeoJSON geometries backed by LiteDB spatial primitives.
/// </summary>
public static class GeoJsonSerializer
{
    /// <summary>
    /// Converts the provided geometry into a GeoJSON <see cref="BaseLiteDB.BsonValue"/> representation.
    /// </summary>
    /// <param name="geometry">The geometry to serialize.</param>
    /// <returns>A <see cref="BaseLiteDB.BsonValue"/> describing the GeoJSON geometry.</returns>
    public static BaseLiteDB.BsonValue ToBson(object? geometry)
    {
        return geometry switch
        {
            null => BaseLiteDB.BsonValue.Null,
            GeoPoint point => ToBson(point),
            GeoLineString line => ToBson(line),
            GeoPolygon polygon => ToBson(polygon),
            _ => throw new BaseLiteDB.LiteException(0, $"Unsupported geometry type '{geometry.GetType().FullName}'.")
        };
    }

    /// <summary>
    /// Converts a <see cref="GeoPoint"/> into GeoJSON.
    /// </summary>
    public static BaseLiteDB.BsonValue ToBson(GeoPoint point)
    {
        return new BaseLiteDB.BsonDocument
        {
            ["type"] = "Point",
            ["coordinates"] = new BaseLiteDB.BsonArray { point.Longitude, point.Latitude }
        };
    }

    /// <summary>
    /// Converts a <see cref="GeoLineString"/> into GeoJSON.
    /// </summary>
    public static BaseLiteDB.BsonValue ToBson(GeoLineString line)
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
    /// Converts a <see cref="GeoPolygon"/> into GeoJSON.
    /// </summary>
    public static BaseLiteDB.BsonValue ToBson(GeoPolygon polygon)
    {
        if (polygon == null)
        {
            throw new ArgumentNullException(nameof(polygon));
        }

        var rings = new BaseLiteDB.BsonArray
        {
            BuildRing(polygon.Outer)
        };

        foreach (var hole in polygon.Holes)
        {
            rings.Add(BuildRing(hole));
        }

        return new BaseLiteDB.BsonDocument
        {
            ["type"] = "Polygon",
            ["coordinates"] = rings
        };
    }

    /// <summary>
    /// Deserializes a GeoJSON <see cref="BaseLiteDB.BsonValue"/> into the requested geometry type.
    /// </summary>
    /// <typeparam name="T">The expected geometry type.</typeparam>
    /// <param name="value">The GeoJSON payload.</param>
    /// <returns>The deserialized geometry.</returns>
    public static T Deserialize<T>(BaseLiteDB.BsonValue value)
    {
        var geometry = Deserialize(value);
        if (geometry is T typed)
        {
            return typed;
        }

        var actual = geometry?.GetType().Name ?? "null";
        throw new BaseLiteDB.LiteException(0, $"GeoJSON payload describes '{actual}' but '{typeof(T).Name}' was requested.");
    }

    /// <summary>
    /// Deserializes a GeoJSON <see cref="BaseLiteDB.BsonValue"/> into a geometry instance.
    /// </summary>
    /// <param name="value">The GeoJSON payload.</param>
    /// <returns>The deserialized geometry object.</returns>
    public static object? Deserialize(BaseLiteDB.BsonValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        if (!value.IsDocument)
        {
            throw new BaseLiteDB.LiteException(0, "GeoJSON payload must be a document containing 'type' and 'coordinates'.");
        }

        var document = value.AsDocument;

        if (document.ContainsKey("crs"))
        {
            throw new BaseLiteDB.LiteException(0, "Only the default WGS84 CRS is supported.");
        }

        if (!document.TryGetValue("type", out var typeValue) || !typeValue.IsString)
        {
            throw new BaseLiteDB.LiteException(0, "GeoJSON requires a string 'type' property.");
        }

        if (!document.TryGetValue("coordinates", out var coordinatesValue) || !coordinatesValue.IsArray)
        {
            throw new BaseLiteDB.LiteException(0, "GeoJSON requires an array 'coordinates' property.");
        }

        var coordinates = coordinatesValue.AsArray;
        var type = typeValue.AsString;

        return type switch
        {
            "Point" => DeserializePoint(coordinates),
            "LineString" => DeserializeLineString(coordinates),
            "Polygon" => DeserializePolygon(coordinates),
            _ => throw new BaseLiteDB.LiteException(0, $"Unsupported GeoJSON geometry type '{type}'.")
        };
    }

    private static GeoPoint DeserializePoint(BaseLiteDB.BsonArray coordinates)
    {
        if (coordinates.Count < 2)
        {
            throw new BaseLiteDB.LiteException(0, "GeoJSON Point coordinates must contain longitude and latitude.");
        }

        return new GeoPoint(coordinates[0].AsDouble, coordinates[1].AsDouble);
    }

    private static GeoLineString DeserializeLineString(BaseLiteDB.BsonArray coordinates)
    {
        if (coordinates.Count < 2)
        {
            throw new BaseLiteDB.LiteException(0, "GeoJSON LineString must contain at least two positions.");
        }

        var points = new List<GeoPoint>(coordinates.Count);
        foreach (var coordinate in coordinates)
        {
            points.Add(ParsePosition(coordinate, "LineString"));
        }

        return new GeoLineString(points);
    }

    private static GeoPolygon DeserializePolygon(BaseLiteDB.BsonArray coordinates)
    {
        if (coordinates.Count == 0)
        {
            throw new BaseLiteDB.LiteException(0, "GeoJSON Polygon must contain at least one ring.");
        }

        var rings = coordinates.Select((value, index) => ParseRing(value, index)).ToList();
        var outer = rings[0];
        var holes = rings.Skip(1).Select(r => (IEnumerable<GeoPoint>)r).ToList();

        return new GeoPolygon(outer, holes);
    }

    private static IReadOnlyList<GeoPoint> ParseRing(BaseLiteDB.BsonValue value, int index)
    {
        if (!value.IsArray)
        {
            throw new BaseLiteDB.LiteException(0, $"GeoJSON Polygon ring at index {index} must be an array of positions.");
        }

        var array = value.AsArray;
        if (array.Count < 4)
        {
            throw new BaseLiteDB.LiteException(0, $"GeoJSON Polygon ring at index {index} must contain at least four positions including closure.");
        }

        var points = new List<GeoPoint>(array.Count);
        foreach (var coordinate in array)
        {
            points.Add(ParsePosition(coordinate, $"Polygon ring {index}"));
        }

        return points;
    }

    private static GeoPoint ParsePosition(BaseLiteDB.BsonValue value, string context)
    {
        if (!value.IsArray)
        {
            throw new BaseLiteDB.LiteException(0, $"{context} positions must be arrays of [longitude, latitude].");
        }

        var array = value.AsArray;
        if (array.Count < 2)
        {
            throw new BaseLiteDB.LiteException(0, $"{context} positions must contain longitude and latitude values.");
        }

        return new GeoPoint(array[0].AsDouble, array[1].AsDouble);
    }

    private static BaseLiteDB.BsonArray BuildRing(IEnumerable<GeoPoint> ring)
    {
        var array = new BaseLiteDB.BsonArray();
        foreach (var point in ring)
        {
            array.Add(new BaseLiteDB.BsonArray { point.Longitude, point.Latitude });
        }

        return array;
    }
}
