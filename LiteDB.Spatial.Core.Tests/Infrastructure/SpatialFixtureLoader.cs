using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using GeoJSON.Net.Converters;
using GeoJSON.Net.Geometry;
using Newtonsoft.Json;

#nullable enable

namespace LiteDB.Spatial.Core.Tests.Infrastructure
{
    public static class SpatialFixtureLoader
    {
        private static readonly Lazy<string> FixturesRoot = new Lazy<string>(ResolveFixturesRoot);
        private static readonly JsonSerializerSettings GeoJsonSettings = new JsonSerializerSettings
        {
            Culture = CultureInfo.InvariantCulture,
            Converters = { new GeometryConverter() }
        };

        public static IReadOnlyList<GeodesicPairFixture> LoadGeodesicPairs()
        {
            var path = Path.Combine(FixturesRoot.Value, "geodesic_pairs.json");
            var payload = File.ReadAllText(path);
            var records = JsonConvert.DeserializeObject<List<GeodesicPairRecord>>(payload, GeoJsonSettings) ?? new List<GeodesicPairRecord>();

            var result = new List<GeodesicPairFixture>(records.Count);
            foreach (var record in records)
            {
                result.Add(new GeodesicPairFixture(
                    record.Id ?? string.Empty,
                    record.Description ?? string.Empty,
                    new GeoCoordinate(record.From?.Lat ?? 0d, record.From?.Lon ?? 0d),
                    new GeoCoordinate(record.To?.Lat ?? 0d, record.To?.Lon ?? 0d)));
            }

            return new ReadOnlyCollection<GeodesicPairFixture>(result);
        }

        public static IReadOnlyList<PolygonFixture> LoadPolygons()
        {
            var directory = Path.Combine(FixturesRoot.Value, "geojson_polygons");
            var files = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly) : Array.Empty<string>();
            var result = new List<PolygonFixture>(files.Length);

            foreach (var file in files)
            {
                var payload = File.ReadAllText(file);
                var record = JsonConvert.DeserializeObject<PolygonRecord>(payload, GeoJsonSettings) ?? new PolygonRecord();
                if (record.GeoJson is Polygon polygon)
                {
                    result.Add(new PolygonFixture(record.Id ?? Path.GetFileNameWithoutExtension(file), record.Description ?? string.Empty, polygon));
                }
            }

            return new ReadOnlyCollection<PolygonFixture>(result);
        }

        public static IReadOnlyList<PointCloudFixture> LoadPointClouds()
        {
            var directory = Path.Combine(FixturesRoot.Value, "point_clouds");
            var files = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly) : Array.Empty<string>();
            var result = new List<PointCloudFixture>(files.Length);

            foreach (var file in files)
            {
                var payload = File.ReadAllText(file);
                var record = JsonConvert.DeserializeObject<PointCloudRecord>(payload, GeoJsonSettings) ?? new PointCloudRecord();
                var points = new List<CartesianPoint>();

                if (record.Points is not null)
                {
                    foreach (var point in record.Points)
                    {
                        points.Add(new CartesianPoint(point.X, point.Y, point.Z));
                    }
                }

                result.Add(new PointCloudFixture(record.Id ?? Path.GetFileNameWithoutExtension(file), record.Description ?? string.Empty, new ReadOnlyCollection<CartesianPoint>(points)));
            }

            return new ReadOnlyCollection<PointCloudFixture>(result);
        }

        private static string ResolveFixturesRoot()
        {
            var baseDir = AppContext.BaseDirectory;
            var path = Path.Combine(baseDir, "..", "..", "..", "..", "tests", "fixtures");
            return Path.GetFullPath(path);
        }

        private sealed class GeodesicPairRecord
        {
            public string? Id { get; set; }

            public string? Description { get; set; }

            public CoordinateRecord? From { get; set; }

            public CoordinateRecord? To { get; set; }
        }

        private sealed class CoordinateRecord
        {
            public double Lat { get; set; }

            public double Lon { get; set; }
        }

        private sealed class PolygonRecord
        {
            public string? Id { get; set; }

            public string? Description { get; set; }

            public IGeometryObject? GeoJson { get; set; }
        }

        private sealed class PointCloudRecord
        {
            public string? Id { get; set; }

            public string? Description { get; set; }

            public List<CartesianPointRecord>? Points { get; set; }
        }

        private sealed class CartesianPointRecord
        {
            public double X { get; set; }

            public double Y { get; set; }

            public double Z { get; set; }
        }
    }

    public readonly record struct GeoCoordinate(double Latitude, double Longitude);

    public sealed record GeodesicPairFixture(string Id, string Description, GeoCoordinate From, GeoCoordinate To);

    public sealed record PolygonFixture(string Id, string Description, Polygon Polygon);

    public sealed record PointCloudFixture(string Id, string Description, IReadOnlyList<CartesianPoint> Points);

    public readonly record struct CartesianPoint(double X, double Y, double Z);
}
