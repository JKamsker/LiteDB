extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using LiteDB.Benchmarks.Models.Spatial;
using LiteDB.Spatial;
using BaseLiteDB = LiteDbBase::LiteDB;
using SpatialFacade = LiteDB.Spatial.Spatial;

namespace LiteDB.Benchmarks.Benchmarks.Spatial
{
    [BenchmarkCategory(Constants.Categories.QUERIES)]
    public class SpatialQueryBenchmarks : BenchmarkBase
    {
        private BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> _collection = null!;
        private SpatialMetadataStore _metadata = null!;
        private SpatialCollectionDescriptor _descriptor = null!;
        private ISpatialEngine _engine = null!;
        private GeoPoint _center;
        private BoundingBox _boundingBox;
        private double _radiusMeters;

        [GlobalSetup]
        public void GlobalSetup()
        {
            File.Delete(DatabasePath);

            DatabaseInstance = new BaseLiteDB.LiteDatabase(ConnectionString());
            _collection = DatabaseInstance.GetCollection("places");
            _metadata = new SpatialMetadataStore(DatabaseInstance);

            SpatialFacade.UseGeographic(_metadata, _collection.Name, "location");
            var documents = SpatialDocumentGenerator.GenerateGeographicDocuments(DatasetSize);
            _collection.Insert(documents);

            _descriptor = SpatialFacade.EnsurePointIndex(_metadata, _collection);
            _engine = _descriptor.Engine ?? throw new InvalidOperationException("Engine should be attached after EnsurePointIndex.");

            DatabaseInstance.Checkpoint();

            _center = new GeoPoint(0, 0);
            _radiusMeters = 25_000;
            _boundingBox = BoundingBox.From2D(-0.2, -0.2, 0.2, 0.2);
        }

        [Benchmark(Baseline = true)]
        public List<BaseLiteDB.BsonDocument> NearFullScan()
        {
            var tolerance = _descriptor.Options.DistanceTolerance;
            var distance = _engine.Distance;

            return _collection.FindAll()
                .Where(document => _engine.Mapper.TryReadPoint(document, out GeoPoint point)
                    && distance.Distance(_center, point) <= _radiusMeters + tolerance)
                .ToList();
        }

        [Benchmark]
        public List<BaseLiteDB.BsonDocument> NearUsingIndex()
        {
            var plan = SpatialFacade.Near(_descriptor, _center, _radiusMeters);
            var tolerance = _descriptor.Options.DistanceTolerance;
            var distance = _engine.Distance;

            return ExecutePlan(plan, document =>
            {
                if (!_engine.Mapper.TryReadPoint(document, out GeoPoint point))
                {
                    return false;
                }

                if (plan.CoveringBounds.HasValue && !Contains(plan.CoveringBounds.Value, point))
                {
                    return false;
                }

                return distance.Distance(_center, point) <= _radiusMeters + tolerance;
            });
        }

        [Benchmark]
        public List<BaseLiteDB.BsonDocument> BoundingBoxFullScan()
        {
            return _collection.FindAll()
                .Where(document => _engine.Mapper.TryReadPoint(document, out GeoPoint point) && Contains(_boundingBox, point))
                .ToList();
        }

        [Benchmark]
        public List<BaseLiteDB.BsonDocument> BoundingBoxUsingIndex()
        {
            var plan = SpatialFacade.WithinBoundingBox(_descriptor, _boundingBox);
            return ExecutePlan(plan, document =>
                _engine.Mapper.TryReadPoint(document, out GeoPoint point) && Contains(_boundingBox, point));
        }

        private List<BaseLiteDB.BsonDocument> ExecutePlan(ISpatialQueryPlan plan, Func<BaseLiteDB.BsonDocument, bool> predicate)
        {
            var results = new List<BaseLiteDB.BsonDocument>();
            var visited = new HashSet<int>();
            var options = _descriptor.Options;

            foreach (var range in plan.IndexRanges)
            {
                var start = ToIndexValue(range.Start);
                var end = ToIndexValue(range.End);
                var query = _collection.Query().Where(BaseLiteDB.Query.Between(options.IndexFieldName, start, end));

                foreach (var document in query.ToDocuments())
                {
                    var id = document["_id"].AsInt32;
                    if (!visited.Add(id))
                    {
                        continue;
                    }

                    if (predicate(document))
                    {
                        results.Add(document);
                    }
                }
            }

            return results;
        }

        private static BaseLiteDB.BsonValue ToIndexValue(ulong value)
        {
            return value <= long.MaxValue
                ? new BaseLiteDB.BsonValue((long)value)
                : new BaseLiteDB.BsonValue((decimal)value);
        }

        private static bool Contains(BoundingBox box, GeoPoint point)
        {
            var values = box.GetValues();
            return point.Longitude >= values[0]
                && point.Latitude >= values[1]
                && point.Longitude <= values[2]
                && point.Latitude <= values[3];
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            DatabaseInstance?.Checkpoint();
            DatabaseInstance?.Dispose();
            DatabaseInstance = null;

            File.Delete(DatabasePath);
        }
    }
}
