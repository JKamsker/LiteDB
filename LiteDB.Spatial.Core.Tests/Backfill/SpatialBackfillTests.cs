extern alias litedb;

using System;
using System.IO;
using FluentAssertions;
using litedb::LiteDB;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Backfill;

public sealed class SpatialBackfillTests
{
    [Fact]
    public void Run_ShouldPopulateMissingFields()
    {
        using var stream = new MemoryStream();
        using var database = new LiteDatabase(stream);
        var collection = database.GetCollection<BsonDocument>("points");

        collection.Insert(new BsonDocument
        {
            ["_id"] = 1,
            ["location"] = new BsonDocument { ["x"] = 0.25, ["y"] = 0.75 }
        });

        var options = new SpatialIndexOptions(precisionBits: 4, indexFieldName: "_idx", boundingBoxFieldName: "_mbb");
        var encoder = new MortonIndexEncoder(2, options.PrecisionBits);
        var mapper = new TestMapper(encoder);
        var engine = new TestEngine("test", 2, options, encoder, mapper);
        var descriptor = new SpatialCollectionDescriptor("points", "test", 2, "location", options).WithEngine(engine);

        var existingIndex = unchecked((long)encoder.Encode(new[] { 0.5, 0.5 }));
        collection.Insert(new BsonDocument
        {
            ["_id"] = 2,
            ["location"] = new BsonDocument { ["x"] = 0.5, ["y"] = 0.5 },
            ["_idx"] = existingIndex,
            ["_mbb"] = new BsonArray { 0.5, 0.5, 0.5, 0.5 }
        });

        var result = SpatialBackfill.Run(collection, descriptor, batchSize: 1);

        result.Updated.Should().Be(1);
        result.Skipped.Should().Be(1);
        result.Errors.Should().Be(0);
        result.LastCheckpoint.Should().NotBeNull();
        result.LastCheckpoint!.AsInt32.Should().Be(2);

        var reloaded = collection.FindById(1);
        reloaded["_idx"].IsInt64.Should().BeTrue();
        reloaded["_mbb"].AsArray.Count.Should().Be(4);

        var secondRun = SpatialBackfill.Run(collection, descriptor);
        secondRun.Updated.Should().Be(0);
        secondRun.Skipped.Should().Be(2);
        secondRun.Errors.Should().Be(0);
    }

    [Fact]
    public void Run_ShouldCountErrorsWithoutStopping()
    {
        using var stream = new MemoryStream();
        using var database = new LiteDatabase(stream);
        var collection = database.GetCollection<BsonDocument>("points");

        collection.Insert(new BsonDocument
        {
            ["_id"] = 1,
            ["location"] = new BsonDocument { ["x"] = 0.1, ["y"] = 0.2 }
        });

        collection.Insert(new BsonDocument
        {
            ["_id"] = 2,
            ["location"] = new BsonDocument { ["x"] = "bad", ["y"] = 0.4 }
        });

        var options = new SpatialIndexOptions(precisionBits: 3, indexFieldName: "_idx", boundingBoxFieldName: "_mbb");
        var encoder = new MortonIndexEncoder(2, options.PrecisionBits);
        var mapper = new TestMapper(encoder);
        var engine = new TestEngine("test", 2, options, encoder, mapper);
        var descriptor = new SpatialCollectionDescriptor("points", "test", 2, "location", options).WithEngine(engine);

        var result = SpatialBackfill.Run(collection, descriptor);

        result.Updated.Should().Be(1);
        result.Skipped.Should().Be(0);
        result.Errors.Should().Be(1);
    }

    private sealed class TestMapper : ISpatialMapper
    {
        private readonly MortonIndexEncoder _encoder;

        public TestMapper(MortonIndexEncoder encoder)
        {
            _encoder = encoder;
        }

        public bool TryExtractPoint(BsonValue value, out GeoPoint point)
        {
            if (value.Type != BsonType.Document)
            {
                point = default;
                return false;
            }

            var doc = value.AsDocument;
            if (!doc.TryGetValue("x", out var xValue) || !doc.TryGetValue("y", out var yValue))
            {
                point = default;
                return false;
            }

            if (!xValue.IsNumber || !yValue.IsNumber)
            {
                point = default;
                return false;
            }

            point = new GeoPoint(xValue.AsDouble, yValue.AsDouble);
            return true;
        }

        public bool TryExtractPoint3D(BsonValue value, out GeoPoint3D point)
        {
            if (value.Type != BsonType.Document)
            {
                point = default;
                return false;
            }

            var doc = value.AsDocument;
            if (!doc.TryGetValue("x", out var xValue) || !doc.TryGetValue("y", out var yValue) || !doc.TryGetValue("z", out var zValue))
            {
                point = default;
                return false;
            }

            if (!xValue.IsNumber || !yValue.IsNumber || !zValue.IsNumber)
            {
                point = default;
                return false;
            }

            point = new GeoPoint3D(xValue.AsDouble, yValue.AsDouble, zValue.AsDouble);
            return true;
        }

        public BoundingBox GetBoundingBox(GeoPoint point)
        {
            return BoundingBox.From2D(point.Longitude, point.Latitude, point.Longitude, point.Latitude);
        }

        public BoundingBox GetBoundingBox(GeoPoint3D point)
        {
            return BoundingBox.From3D(point.X, point.Y, point.Z, point.X, point.Y, point.Z);
        }

        public ulong Encode(GeoPoint point)
        {
            return _encoder.Encode(new[] { point.Longitude, point.Latitude });
        }

        public ulong Encode(GeoPoint3D point)
        {
            return _encoder.Encode(new[] { point.X, point.Y, point.Z });
        }
    }

    private sealed class TestEngine : ISpatialEngine
    {
        public TestEngine(string name, int dimensions, SpatialIndexOptions options, ISpatialIndexEncoder encoder, ISpatialMapper mapper)
        {
            Name = name;
            Dimensions = dimensions;
            Options = options;
            IndexEncoder = encoder;
            Mapper = mapper;
            Distance = new TestDistance();
        }

        public string Name { get; }

        public int Dimensions { get; }

        public SpatialIndexOptions Options { get; }

        public ISpatialIndexEncoder IndexEncoder { get; }

        public ISpatialMapper Mapper { get; }

        public ISpatialDistance Distance { get; }

        public ISpatialQueryPlan PlanNear(GeoPoint center, double radius) => throw new NotSupportedException();

        public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius) => throw new NotSupportedException();

        public ISpatialQueryPlan PlanWithin(BoundingBox bounds) => throw new NotSupportedException();
    }

    private sealed class TestDistance : ISpatialDistance
    {
        public double Distance(GeoPoint left, GeoPoint right) => 0;

        public double Distance(GeoPoint3D left, GeoPoint3D right) => 0;
    }
}
