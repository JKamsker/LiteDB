using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using LiteDB.Spatial.Core.Tests.Infrastructure.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OracleGeoPoint2D = LiteDB.Spatial.Testing.Oracles.Abstractions.GeoPoint2D;
using OracleGeoPoint3D = LiteDB.Spatial.Testing.Oracles.Abstractions.GeoPoint3D;
using OracleGeoCoordinate = LiteDB.Spatial.Testing.Oracles.Abstractions.GeoCoordinate;

namespace LiteDB.Spatial.Core.Tests.Infrastructure;

internal static class FixtureLoader
{
    public static IReadOnlyList<GeodesicPairFixture> LoadGeodesicPairs()
    {
        using var stream = File.OpenRead(FixturePaths.GeodesicPairs);
        using var document = JsonDocument.Parse(stream);
        var pairs = new List<GeodesicPairFixture>();
        foreach (var element in document.RootElement.GetProperty("pairs").EnumerateArray())
        {
            var id = element.GetProperty("id").GetString() ?? "unknown";
            var start = element.GetProperty("start");
            var end = element.GetProperty("end");
            var expected = element.GetProperty("expected_meters").GetDouble();
            pairs.Add(new GeodesicPairFixture(
                id,
                new OracleGeoCoordinate(start.GetProperty("lat").GetDouble(), start.GetProperty("lon").GetDouble()),
                new OracleGeoCoordinate(end.GetProperty("lat").GetDouble(), end.GetProperty("lon").GetDouble()),
                expected));
        }

        return pairs;
    }

    public static IReadOnlyList<PolygonFixture> LoadPolygons()
    {
        var fixtures = new List<PolygonFixture>();
        foreach (var file in Directory.GetFiles(FixturePaths.GeoJsonPolygons, "*.json", SearchOption.TopDirectoryOnly))
        {
            var json = File.ReadAllText(file);
            var token = JObject.Parse(json);
            var id = token.Value<string>("id") ?? Path.GetFileNameWithoutExtension(file);
            var description = token.Value<string>("description");
            var geoJsonToken = token["geojson"] ?? new JObject();
            var rawGeoJson = geoJsonToken.ToString();
            var feature = JsonConvert.DeserializeObject<Feature>(rawGeoJson);
            if (feature?.Geometry is not Polygon polygon)
            {
                continue;
            }

            var rings = polygon.Coordinates
                .Select(ring => (IReadOnlyList<OracleGeoPoint2D>)ring.Coordinates
                    .Select(position => new OracleGeoPoint2D(position.Longitude, position.Latitude))
                    .ToList())
                .ToList();

            var expectedArea = token.Value<double?>("expected_area");
            var expectedPerimeter = token.Value<double?>("expected_perimeter");

            fixtures.Add(new PolygonFixture(id, description, rawGeoJson, rings, expectedArea, expectedPerimeter));
        }

        return fixtures;
    }

    public static IReadOnlyList<PointCloudFixture> LoadPointClouds()
    {
        var fixtures = new List<PointCloudFixture>();
        foreach (var file in Directory.GetFiles(FixturePaths.PointClouds, "*.json", SearchOption.TopDirectoryOnly))
        {
            using var stream = File.OpenRead(file);
            using var document = JsonDocument.Parse(stream);
            var id = document.RootElement.GetProperty("id").GetString() ?? Path.GetFileNameWithoutExtension(file);
            var points = document.RootElement.GetProperty("points")
                .EnumerateArray()
                .Select(point => new OracleGeoPoint3D(
                    point[0].GetDouble(),
                    point[1].GetDouble(),
                    point[2].GetDouble()))
                .ToList();

            fixtures.Add(new PointCloudFixture(id, points));
        }

        return fixtures;
    }
}
