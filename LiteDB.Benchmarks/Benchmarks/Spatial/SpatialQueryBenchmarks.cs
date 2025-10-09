extern alias LiteDbSpatial;
extern alias LiteDbSpatialCore;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using LiteDB.Benchmarks.Models.Spatial;
using BoundingBox = LiteDbSpatialCore::LiteDB.Spatial.BoundingBox;
using GeoPoint = LiteDbSpatialCore::LiteDB.Spatial.GeoPoint;
using SpatialFacade = LiteDbSpatial::LiteDB.Spatial.Spatial;

namespace LiteDB.Benchmarks.Benchmarks.Spatial
{
    [BenchmarkCategory(Constants.Categories.QUERIES)]
    public class SpatialQueryBenchmarks : BenchmarkBase
    {
        private ILiteCollection<SpatialDocument> _collection = null!;
        private GeoPoint _center = default;
        private double _radiusMeters;
        private BoundingBox _searchBounds;

        [GlobalSetup]
        public void GlobalSetup()
        {
            File.Delete(DatabasePath);

            DatabaseInstance = new LiteDatabase(ConnectionString());
            _collection = DatabaseInstance.GetCollection<SpatialDocument>("places");

            SpatialFacade.UseGeographic(_collection);
            SpatialFacade.EnsurePointIndex(_collection, x => x.Location);

            var documents = SpatialDocumentGenerator.Generate(DatasetSize);
            _collection.Insert(documents);

            DatabaseInstance.Checkpoint();

            _center = new GeoPoint(0, 0);
            _radiusMeters = 25_000;
            _searchBounds = SpatialDocumentGenerator.BuildSearchBounds(0, 0, 0.2);
        }

        [Benchmark(Baseline = true)]
        public List<SpatialDocument> NearQuery()
        {
            return SpatialFacade.Near(_collection, x => x.Location, _center, _radiusMeters).ToList();
        }

        [Benchmark]
        public List<SpatialDocument> BoundingBoxQuery()
        {
            return SpatialFacade.WithinBoundingBox(_collection, x => x.Location, _searchBounds).ToList();
        }

        [Benchmark]
        public List<SpatialDocument> FullScanNearBaseline()
        {
            return FullScanNear();
        }

        [Benchmark]
        public List<SpatialDocument> FullScanBoundingBoxBaseline()
        {
            return FullScanBoundingBox();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            DatabaseInstance?.Checkpoint();
            DatabaseInstance?.Dispose();
            DatabaseInstance = null;

            File.Delete(DatabasePath);
        }

        private List<SpatialDocument> FullScanNear()
        {
            var results = new List<SpatialDocument>();
            foreach (var document in _collection.FindAll())
            {
                var distance = HaversineDistance(_center, document.Location);
                if (distance <= _radiusMeters)
                {
                    results.Add(document);
                }
            }

            return results;
        }

        private List<SpatialDocument> FullScanBoundingBox()
        {
            var minLon = _searchBounds.MinX;
            var minLat = _searchBounds.MinY;
            var maxLon = _searchBounds.MaxX;
            var maxLat = _searchBounds.MaxY;

            var results = new List<SpatialDocument>();
            foreach (var document in _collection.FindAll())
            {
                var location = document.Location;
                if (location.Longitude >= minLon && location.Longitude <= maxLon
                    && location.Latitude >= minLat && location.Latitude <= maxLat)
                {
                    results.Add(document);
                }
            }

            return results;
        }

        private static double HaversineDistance(GeoPoint left, GeoPoint right)
        {
            const double EarthRadius = 6_371_000d;
            var lat1 = DegreesToRadians(left.Latitude);
            var lat2 = DegreesToRadians(right.Latitude);
            var deltaLat = DegreesToRadians(right.Latitude - left.Latitude);
            var deltaLon = DegreesToRadians(right.Longitude - left.Longitude);

            var sinLat = Math.Sin(deltaLat / 2d);
            var sinLon = Math.Sin(deltaLon / 2d);
            var a = sinLat * sinLat + Math.Cos(lat1) * Math.Cos(lat2) * sinLon * sinLon;
            var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
            return EarthRadius * c;
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180d;
        }
    }
}
