extern alias SpatialFacade;
extern alias SpatialCore;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using LiteDB.Benchmarks.Models.Spatial;
using BoundingBox = SpatialCore::LiteDB.Spatial.BoundingBox;
using GeoPoint = SpatialCore::LiteDB.Spatial.GeoPoint;
using SpatialApi = SpatialFacade::LiteDB.Spatial.Spatial;

namespace LiteDB.Benchmarks.Benchmarks.Spatial
{
    [BenchmarkCategory(Constants.Categories.QUERIES)]
    public class SpatialQueryBenchmarks : BenchmarkBase
    {
        private ILiteCollection<SpatialDocument> _collection = null!;
        private GeoPoint _center;
        private BoundingBox _searchBounds;
        private double _radiusMeters;

        [GlobalSetup]
        public void GlobalSetup()
        {
            File.Delete(DatabasePath);

            DatabaseInstance = new LiteDatabase(ConnectionString());
            _collection = DatabaseInstance.GetCollection<SpatialDocument>("places");

            SpatialApi.UseGeographic(_collection, x => x.Location);

            var documents = SpatialDocumentGenerator.Generate(DatasetSize);
            _collection.Insert(documents);

            DatabaseInstance.Checkpoint();

            _center = new GeoPoint(0, 0);
            _radiusMeters = 25_000;
            _searchBounds = BoundingBox.From2D(-0.1, -0.1, 0.1, 0.1);
        }

        [Benchmark(Baseline = true)]
        public List<SpatialDocument> NearQuery()
        {
            return SpatialApi.Near(_collection, x => x.Location, _center, _radiusMeters).ToList();
        }

        [Benchmark]
        public List<SpatialDocument> BoundingBoxQuery()
        {
            return SpatialApi.WithinBoundingBox(_collection, x => x.Location, _searchBounds).ToList();
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
