using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeoJSON.Net.Converters;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using LiteDB.Spatial.Testing.Oracles.Infrastructure;
using LiteDB.Spatial.Testing.Oracles.Interfaces;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using GeoJsonGeometryCollection = GeoJSON.Net.Geometry.GeometryCollection;
using GeoJsonPolygon = GeoJSON.Net.Geometry.Polygon;
using GeoJsonMultiPolygon = GeoJSON.Net.Geometry.MultiPolygon;
using GeoJsonLineString = GeoJSON.Net.Geometry.LineString;
using GeoJsonMultiLineString = GeoJSON.Net.Geometry.MultiLineString;
using GeoJsonPoint = GeoJSON.Net.Geometry.Point;
using GeoJsonMultiPoint = GeoJSON.Net.Geometry.MultiPoint;

namespace LiteDB.Spatial.Testing.Oracles.Adapters;

/// <summary>
/// Thin wrapper around NetTopologySuite predicates.
/// </summary>
public sealed class NtsOracle : IGeometryOracle2D
{
    private readonly GeometryFactory _geometryFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="NtsOracle"/> class.
    /// </summary>
    public NtsOracle()
    {
        _geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
    }

    /// <inheritdoc />
    public bool IsAvailable => OracleEnvironment.AreOraclesEnabled;

    /// <inheritdoc />
    public GeometryHandle LoadFromGeoJson(string geoJson)
    {
        OracleEnvironment.EnsureOraclesEnabled(nameof(NtsOracle));

        using var reader = new JsonTextReader(new StringReader(geoJson));
        var serializer = JsonSerializer.CreateDefault();
        serializer.Converters.Add(new GeometryConverter());

        var token = JToken.ReadFrom(reader);
        var type = token.Value<string>("type");

        IGeometryObject geometryObject = type switch
        {
            "Feature" => DeserializeGeometry(token["geometry"], serializer),
            "FeatureCollection" => CreateCollection(token["features"], serializer),
            _ => DeserializeGeometry(token, serializer)
        };

        var ntsGeometry = ToGeometry(geometryObject);
        return new GeometryHandle(ntsGeometry);
    }

    /// <inheritdoc />
    public GeometryHandle CreatePolygon(IReadOnlyList<PlanarCoordinate> shell, IReadOnlyList<IReadOnlyList<PlanarCoordinate>>? holes = null)
    {
        OracleEnvironment.EnsureOraclesEnabled(nameof(NtsOracle));

        if (shell.Count < 4)
        {
            throw new ArgumentException("The shell must contain at least four positions (closed ring).", nameof(shell));
        }

        var shellCoordinates = shell.Select(ToCoordinate).ToArray();
        var linearShell = _geometryFactory.CreateLinearRing(shellCoordinates);

        LinearRing[]? holeRings = null;
        if (holes != null && holes.Count > 0)
        {
            holeRings = new LinearRing[holes.Count];
            for (var i = 0; i < holes.Count; i++)
            {
                var hole = holes[i];
                if (hole.Count < 4)
                {
                    throw new ArgumentException("Each hole must contain at least four positions (closed ring).", nameof(holes));
                }

                holeRings[i] = _geometryFactory.CreateLinearRing(hole.Select(ToCoordinate).ToArray());
            }
        }

        var polygon = _geometryFactory.CreatePolygon(linearShell, holeRings);
        return new GeometryHandle(polygon);
    }

    /// <inheritdoc />
    public bool Contains(GeometryHandle geometry, PlanarCoordinate point)
    {
        OracleEnvironment.EnsureOraclesEnabled(nameof(NtsOracle));
        var ntsGeometry = ExtractGeometry(geometry);
        var pointGeometry = _geometryFactory.CreatePoint(new Coordinate(point.X, point.Y));
        return ntsGeometry.Contains(pointGeometry);
    }

    /// <inheritdoc />
    public bool Intersects(GeometryHandle left, GeometryHandle right)
    {
        OracleEnvironment.EnsureOraclesEnabled(nameof(NtsOracle));
        var leftGeometry = ExtractGeometry(left);
        var rightGeometry = ExtractGeometry(right);
        return leftGeometry.Intersects(rightGeometry);
    }

    /// <inheritdoc />
    public bool Within(GeometryHandle inner, GeometryHandle outer)
    {
        OracleEnvironment.EnsureOraclesEnabled(nameof(NtsOracle));
        var innerGeometry = ExtractGeometry(inner);
        var outerGeometry = ExtractGeometry(outer);
        return innerGeometry.Within(outerGeometry);
    }

    private Geometry ExtractGeometry(GeometryHandle handle)
    {
        if (handle.NativeGeometry is not Geometry geometry)
        {
            throw new InvalidOperationException("The provided handle is not managed by the NTS oracle.");
        }

        return geometry;
    }

    private Geometry ToGeometry(IGeometryObject geometryObject)
    {
        return geometryObject switch
        {
            GeoJsonPoint point => _geometryFactory.CreatePoint(ToCoordinate(point.Coordinates)),
            GeoJsonMultiPoint multiPoint => _geometryFactory.CreateMultiPoint(multiPoint.Coordinates.Select(c => _geometryFactory.CreatePoint(ToCoordinate(c.Coordinates))).ToArray()),
            GeoJsonLineString lineString => _geometryFactory.CreateLineString(lineString.Coordinates.Select(ToCoordinate).ToArray()),
            GeoJsonMultiLineString multiLine => _geometryFactory.CreateMultiLineString(multiLine.Coordinates.Select(ls => _geometryFactory.CreateLineString(ls.Coordinates.Select(ToCoordinate).ToArray())).ToArray()),
            GeoJsonPolygon polygon => CreatePolygonFromGeoJson(polygon),
            GeoJsonMultiPolygon multiPolygon => _geometryFactory.CreateMultiPolygon(multiPolygon.Coordinates.Select(CreatePolygonFromGeoJson).ToArray()),
            GeoJsonGeometryCollection collection => _geometryFactory.CreateGeometryCollection(collection.Geometries.Select(ToGeometry).ToArray()),
            _ => throw new InvalidOperationException($"Unsupported GeoJSON geometry type '{geometryObject.Type}'.")
        };
    }

    private NetTopologySuite.Geometries.Polygon CreatePolygonFromGeoJson(GeoJsonPolygon polygon)
    {
        if (polygon.Coordinates.Count == 0)
        {
            throw new InvalidOperationException("A GeoJSON polygon must contain at least one linear ring.");
        }

        var shell = polygon.Coordinates[0];
        var shellRing = _geometryFactory.CreateLinearRing(shell.Coordinates.Select(c => ToCoordinate(c)).ToArray());
        var holes = polygon.Coordinates.Skip(1)
            .Select(ring => _geometryFactory.CreateLinearRing(ring.Coordinates.Select(c => ToCoordinate(c)).ToArray()))
            .ToArray();
        return _geometryFactory.CreatePolygon(shellRing, holes);
    }

    private static Coordinate ToCoordinate(PlanarCoordinate point)
    {
        return new Coordinate(point.X, point.Y);
    }

    private static Coordinate ToCoordinate(IPosition position)
    {
        return new Coordinate(position.Longitude, position.Latitude);
    }

    private static IGeometryObject DeserializeGeometry(JToken? token, JsonSerializer serializer)
    {
        if (token == null)
        {
            throw new InvalidOperationException("Feature is missing geometry.");
        }

        using var geometryReader = token.CreateReader();
        return serializer.Deserialize<IGeometryObject>(geometryReader) ?? throw new InvalidOperationException("Unsupported GeoJSON geometry.");
    }

    private static GeoJsonGeometryCollection CreateCollection(JToken? featuresToken, JsonSerializer serializer)
    {
        if (featuresToken is not JArray array)
        {
            throw new InvalidOperationException("Feature collection must contain an array of features.");
        }

        var geometries = new List<IGeometryObject>(array.Count);
        foreach (var feature in array)
        {
            geometries.Add(DeserializeGeometry(feature["geometry"], serializer));
        }

        return new GeoJsonGeometryCollection(geometries);
    }
}
