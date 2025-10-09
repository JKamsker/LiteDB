extern alias litedbmain;

using System;
using System.IO;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;
using LiteDatabase = litedbmain::LiteDB.LiteDatabase;
using LiteDbBsonDocument = litedbmain::LiteDB.BsonDocument;
using LiteDbBsonValue = litedbmain::LiteDB.BsonValue;
using SpatialGeoPoint = LiteDB.Spatial.GeoPoint;
using SpatialGeoPoint3D = LiteDB.Spatial.GeoPoint3D;

namespace LiteDB.Spatial.Core.Tests.Backfill;

public class SpatialBackfillTests
{
    [Fact]
    public void Run_ShouldPopulateMissingFields()
    {
        using var database = new LiteDatabase(new MemoryStream());
        var collection = database.GetCollection("points");
        var doc = new LiteDbBsonDocument
        {
            ["_id"] = 1,
            ["lon"] = 0.25,
            ["lat"] = 0.75
        };

        collection.Insert(doc);

        var metadata = new SpatialCollectionMetadata("points", "stub", 2, new SpatialIndexOptions(precisionBits: 8));
        var descriptor = new SpatialCollectionDescriptor(metadata, new TestMapper(dimensions: 2), d => new SpatialGeoPoint(d["lon"].AsDouble, d["lat"].AsDouble), null);

        var result = SpatialBackfill.Run(collection, descriptor, batchSize: 16);

        result.Processed.Should().Be(1);
        result.Updated.Should().Be(1);
        result.Errors.Should().BeEmpty();

        var stored = collection.FindById(new LiteDbBsonValue(1));
        ((object)stored).Should().NotBeNull();
        var storedDocument = stored!;
        storedDocument["_idx"].IsDecimal.Should().BeTrue();
        storedDocument["_mbb"].AsArray.Count.Should().Be(4);
    }

    [Fact]
    public void Run_ShouldSkipAlreadyIndexedDocuments()
    {
        using var database = new LiteDatabase(new MemoryStream());
        var collection = database.GetCollection("points");
        var mapper = new TestMapper(dimensions: 2);

        var doc = new LiteDbBsonDocument
        {
            ["_id"] = 1,
            ["lon"] = 0.1,
            ["lat"] = 0.2
        };

        collection.Insert(doc);

        var metadata = new SpatialCollectionMetadata("points", "stub", 2, new SpatialIndexOptions(precisionBits: 6));
        var descriptor = new SpatialCollectionDescriptor(metadata, mapper, d => new SpatialGeoPoint(d["lon"].AsDouble, d["lat"].AsDouble), null);

        SpatialBackfill.Run(collection, descriptor, batchSize: 16);
        var rerun = SpatialBackfill.Run(collection, descriptor, batchSize: 16);

        rerun.Processed.Should().Be(1);
        rerun.Updated.Should().Be(0);
        rerun.Skipped.Should().Be(1);
        rerun.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Run_ShouldCollectErrorsForDocumentsMissingGeometry()
    {
        using var database = new LiteDatabase(new MemoryStream());
        var collection = database.GetCollection("points");

        collection.Insert(new LiteDbBsonDocument { ["_id"] = 1 });

        var metadata = new SpatialCollectionMetadata("points", "stub", 2, new SpatialIndexOptions(precisionBits: 6));
        var descriptor = new SpatialCollectionDescriptor(metadata, new TestMapper(dimensions: 2), d => null, null);

        var result = SpatialBackfill.Run(collection, descriptor, batchSize: 8);

        result.Processed.Should().Be(1);
        result.Updated.Should().Be(0);
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Contain("geometry");
    }

    private sealed class TestMapper : ISpatialMapper
    {
        private readonly MortonIndexEncoder _encoder;

        public TestMapper(int dimensions)
        {
            _encoder = new MortonIndexEncoder(dimensions, precisionBits: 8);
        }

        public BoundingBox GetBoundingBox(SpatialGeoPoint point)
        {
            return BoundingBox.From2D(point.Longitude, point.Latitude, point.Longitude, point.Latitude);
        }

        public BoundingBox GetBoundingBox(SpatialGeoPoint3D point)
        {
            return BoundingBox.From3D(point.X, point.Y, point.Z, point.X, point.Y, point.Z);
        }

        public ulong Encode(SpatialGeoPoint point)
        {
            return _encoder.Encode(new[] { point.Longitude, point.Latitude });
        }

        public ulong Encode(SpatialGeoPoint3D point)
        {
            return _encoder.Encode(new[] { point.X, point.Y, point.Z });
        }
    }
}
