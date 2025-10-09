extern alias litedb;
using System;
using System.IO;
using FluentAssertions;
using LiteDbRuntime = litedb::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Backfill;

public class SpatialBackfillTests
{
    [Fact]
    public void Run_ShouldPopulateMissingFieldsAndTrackCounters()
    {
        using var database = new LiteDbRuntime.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<LiteDbRuntime.BsonDocument>("docs");
        var engine = CreateEngine();
        var descriptor = new LiteDB.Spatial.SpatialCollectionDescriptor(engine.Name, engine.Dimensions, engine.Options, engine);

        collection.Insert(new LiteDbRuntime.BsonDocument
        {
            ["_id"] = 1,
            ["point"] = new LiteDbRuntime.BsonArray { 0.1, 0.2 }
        });

        collection.Insert(new LiteDbRuntime.BsonDocument
        {
            ["_id"] = 2,
            ["point"] = new LiteDbRuntime.BsonArray { 0.3, 0.4 }
        });

        collection.Insert(new LiteDbRuntime.BsonDocument
        {
            ["_id"] = 3,
            ["point"] = new LiteDbRuntime.BsonArray { 0.5, 0.6 },
            ["_idx"] = new LiteDbRuntime.BsonValue(42),
            ["_mbb"] = new LiteDbRuntime.BsonArray { 0.5, 0.6, 0.5, 0.6 }
        });

        collection.Insert(new LiteDbRuntime.BsonDocument
        {
            ["_id"] = 4,
            ["value"] = 99
        });

        var result = LiteDB.Spatial.SpatialBackfill.Run(collection, descriptor, batchSize: 2);

        result.UpdatedCount.Should().Be(2);
        result.SkippedCount.Should().Be(1);
        result.ErrorCount.Should().Be(1);
        result.LastCheckpoint.Should().Be(new LiteDbRuntime.BsonValue(4));

        var updated = collection.FindById(1);
        ((object)updated).Should().NotBeNull();
        var updatedDocument = updated!;
        updatedDocument["_idx"].Should().NotBeNull();
        updatedDocument["_mbb"].AsArray.Count.Should().Be(4);

        var rerun = LiteDB.Spatial.SpatialBackfill.Run(collection, descriptor, batchSize: 2);
        rerun.UpdatedCount.Should().Be(0);
        rerun.SkippedCount.Should().Be(3);
        rerun.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void Run_ShouldRespectResumeCheckpoint()
    {
        using var database = new LiteDbRuntime.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<LiteDbRuntime.BsonDocument>("docs");
        var engine = CreateEngine();
        var descriptor = new LiteDB.Spatial.SpatialCollectionDescriptor(engine.Name, engine.Dimensions, engine.Options, engine);

        for (var i = 1; i <= 4; i++)
        {
            collection.Insert(new LiteDbRuntime.BsonDocument
            {
                ["_id"] = i,
                ["point"] = new LiteDbRuntime.BsonArray { 0.1 * i, 0.1 * i }
            });
        }

        var firstRun = LiteDB.Spatial.SpatialBackfill.Run(collection, descriptor, batchSize: 2, resumeAfter: new LiteDbRuntime.BsonValue(2));

        firstRun.UpdatedCount.Should().Be(2);
        firstRun.SkippedCount.Should().Be(0);
        firstRun.ErrorCount.Should().Be(0);
        firstRun.LastCheckpoint.Should().Be(new LiteDbRuntime.BsonValue(4));
    }

    [Fact]
    public void Run_ShouldThrowWhenBoundingBoxLengthMismatch()
    {
        using var database = new LiteDbRuntime.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<LiteDbRuntime.BsonDocument>("docs");
        var engine = CreateEngine();
        var descriptor = new LiteDB.Spatial.SpatialCollectionDescriptor(engine.Name, engine.Dimensions, engine.Options, engine);

        collection.Insert(new LiteDbRuntime.BsonDocument
        {
            ["_id"] = 1,
            ["point"] = new LiteDbRuntime.BsonArray { 0.1, 0.1 },
            ["_idx"] = new LiteDbRuntime.BsonValue(1),
            ["_mbb"] = new LiteDbRuntime.BsonArray { 0.1, 0.1, 0.1 }
        });

        Action act = () => LiteDB.Spatial.SpatialBackfill.Run(collection, descriptor, batchSize: 1);

        act.Should().Throw<LiteDbRuntime.LiteException>()
            .Which.Message.Should().Contain("Expected 4 values");
    }

    private static TestSpatialEngine CreateEngine()
    {
        var options = new LiteDB.Spatial.SpatialIndexOptions(precisionBits: 8);
        return new TestSpatialEngine(2, options);
    }

    private sealed class TestSpatialEngine : LiteDB.Spatial.ISpatialEngine
    {
        public TestSpatialEngine(int dimensions, LiteDB.Spatial.SpatialIndexOptions options)
        {
            Dimensions = dimensions;
            Options = options;
            Name = dimensions == 3 ? "Test3D" : "Test2D";
            IndexEncoder = new LiteDB.Spatial.MortonIndexEncoder(dimensions, options.PrecisionBits);
            Mapper = new TestSpatialMapper(IndexEncoder, dimensions);
            Distance = new TestSpatialDistance();
        }

        public string Name { get; }

        public int Dimensions { get; }

        public LiteDB.Spatial.SpatialIndexOptions Options { get; }

        public LiteDB.Spatial.ISpatialIndexEncoder IndexEncoder { get; }

        public LiteDB.Spatial.ISpatialMapper Mapper { get; }

        public LiteDB.Spatial.ISpatialDistance Distance { get; }

        public LiteDB.Spatial.ISpatialQueryPlan PlanNear(global::LiteDB.Spatial.GeoPoint center, double radius) => throw new NotSupportedException();

        public LiteDB.Spatial.ISpatialQueryPlan PlanNear(global::LiteDB.Spatial.GeoPoint3D center, double radius) => throw new NotSupportedException();

        public LiteDB.Spatial.ISpatialQueryPlan PlanWithin(global::LiteDB.Spatial.BoundingBox bounds) => throw new NotSupportedException();
    }

        private sealed class TestSpatialMapper : LiteDB.Spatial.ISpatialMapper
        {
            private readonly LiteDB.Spatial.ISpatialIndexEncoder _encoder;
            private readonly int _dimensions;

            public TestSpatialMapper(LiteDB.Spatial.ISpatialIndexEncoder encoder, int dimensions)
            {
                _encoder = encoder;
                _dimensions = dimensions;
            }

        public LiteDB.Spatial.BoundingBox GetBoundingBox(global::LiteDB.Spatial.GeoPoint point)
        {
            return global::LiteDB.Spatial.BoundingBox.From2D(point.Longitude, point.Latitude, point.Longitude, point.Latitude);
        }

        public LiteDB.Spatial.BoundingBox GetBoundingBox(global::LiteDB.Spatial.GeoPoint3D point)
        {
            return global::LiteDB.Spatial.BoundingBox.From3D(point.X, point.Y, point.Z, point.X, point.Y, point.Z);
        }

        public ulong Encode(global::LiteDB.Spatial.GeoPoint point)
        {
            return _encoder.Encode(new[] { point.Longitude, point.Latitude });
        }

        public ulong Encode(global::LiteDB.Spatial.GeoPoint3D point)
        {
            return _encoder.Encode(new[] { point.X, point.Y, point.Z });
        }

        public bool TryMapDocument(LiteDbRuntime.BsonDocument document, LiteDB.Spatial.SpatialIndexOptions options, out ulong index, out LiteDB.Spatial.BoundingBox boundingBox)
        {
            if (!document.TryGetValue("point", out var value) || !value.IsArray)
            {
                index = default;
                boundingBox = default;
                return false;
            }

            var array = value.AsArray;

            if (_dimensions == 2 && array.Count >= 2)
            {
                var point = new global::LiteDB.Spatial.GeoPoint(array[0].AsDouble, array[1].AsDouble);
                boundingBox = GetBoundingBox(point);
                index = Encode(point);
                return true;
            }

            if (_dimensions == 3 && array.Count >= 3)
            {
                var point = new global::LiteDB.Spatial.GeoPoint3D(array[0].AsDouble, array[1].AsDouble, array[2].AsDouble);
                boundingBox = GetBoundingBox(point);
                index = Encode(point);
                return true;
            }

            index = default;
            boundingBox = default;
            return false;
        }
    }

    private sealed class TestSpatialDistance : LiteDB.Spatial.ISpatialDistance
    {
        public double Distance(global::LiteDB.Spatial.GeoPoint left, global::LiteDB.Spatial.GeoPoint right) => 0d;

        public double Distance(global::LiteDB.Spatial.GeoPoint3D left, global::LiteDB.Spatial.GeoPoint3D right) => 0d;
    }
}
