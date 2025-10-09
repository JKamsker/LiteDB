extern alias LiteDbBase;

#nullable enable

using System;
using System.IO;

using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using SpatialGeoPoint = LiteDB.Spatial.GeoPoint;
using SpatialGeoPoint3D = LiteDB.Spatial.GeoPoint3D;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Backfill;

public sealed class SpatialBackfillTests
{
    [Fact]
    public void RunBackfillsMissingFieldsAndIsIdempotent()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection("points");
        var descriptor = new LiteDB.Spatial.SpatialCollectionDescriptor("points", "Stub2D", 2, "location", new LiteDB.Spatial.SpatialIndexOptions(precisionBits: 4));
        var engine = new Stub2DEngine(descriptor.Options);

        InsertPoint(collection, 1, 0.25, 0.75);
        InsertPoint(collection, 2, 0.5, 0.5);

        var first = LiteDB.Spatial.SpatialBackfill.Run(collection, descriptor, engine);

        first.Processed.Should().Be(2);
        first.Updated.Should().Be(2);
        first.Skipped.Should().Be(0);
        first.Errors.Should().BeEmpty();

        var stored = collection.FindById(1);
        ((object?)stored).Should().NotBeNull();
        stored![descriptor.Options.IndexFieldName].Type.Should().BeOneOf(BaseLiteDB.BsonType.Int32, BaseLiteDB.BsonType.Int64, BaseLiteDB.BsonType.Decimal);
        stored[descriptor.Options.BoundingBoxFieldName].AsArray.Count.Should().Be(4);

        var second = LiteDB.Spatial.SpatialBackfill.Run(collection, descriptor, engine);
        second.Processed.Should().Be(2);
        second.Updated.Should().Be(0);
        second.Skipped.Should().Be(2);
    }

    [Fact]
    public void RunRecordsErrorsWithoutInterruptingOtherDocuments()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection("points");
        var descriptor = new LiteDB.Spatial.SpatialCollectionDescriptor("points", "Stub2D", 2, "location", new LiteDB.Spatial.SpatialIndexOptions(precisionBits: 4));
        var engine = new Stub2DEngine(descriptor.Options);

        InsertPoint(collection, 1, 0.1, 0.1);
        collection.Insert(new BaseLiteDB.BsonDocument
        {
            ["_id"] = 2,
            ["name"] = "missing-location"
        });
        InsertPoint(collection, 3, 0.9, 0.9);

        var result = LiteDB.Spatial.SpatialBackfill.Run(collection, descriptor, engine);

        result.Processed.Should().Be(3);
        result.Updated.Should().Be(2);
        result.Errors.Should().HaveCount(1);
        result.Errors[0].DocumentId.Should().Be(new BaseLiteDB.BsonValue(2));
    }

    [Fact]
    public void RunHonorsCheckpoint()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection("points");
        var descriptor = new LiteDB.Spatial.SpatialCollectionDescriptor("points", "Stub2D", 2, "location", new LiteDB.Spatial.SpatialIndexOptions(precisionBits: 4));
        var engine = new Stub2DEngine(descriptor.Options);

        InsertPoint(collection, 1, 0.0, 0.0);
        InsertPoint(collection, 2, 0.25, 0.25);
        InsertPoint(collection, 3, 0.5, 0.5);

        var result = LiteDB.Spatial.SpatialBackfill.Run(collection, descriptor, engine, batchSize: 1, checkpoint: new BaseLiteDB.BsonValue(1));

        result.Processed.Should().Be(2);
        result.LastCheckpoint.Should().Be(new BaseLiteDB.BsonValue(3));
        collection.FindById(1)!.ContainsKey(descriptor.Options.IndexFieldName).Should().BeFalse();
        collection.FindById(2)![descriptor.Options.IndexFieldName].Type.Should().NotBe(BaseLiteDB.BsonType.Null);
    }

    private static void InsertPoint(BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection, int id, double x, double y)
    {
        collection.Insert(new BaseLiteDB.BsonDocument
        {
            ["_id"] = id,
            ["location"] = new BaseLiteDB.BsonDocument
            {
                ["x"] = x,
                ["y"] = y
            }
        });
    }

    private sealed class Stub2DEngine : LiteDB.Spatial.ISpatialEngine
    {
        public Stub2DEngine(LiteDB.Spatial.SpatialIndexOptions options)
        {
            Options = options;
            IndexEncoder = new LiteDB.Spatial.MortonIndexEncoder(2, options.PrecisionBits);
            Mapper = new Stub2DMapper(IndexEncoder);
            Distance = new StubDistance();
        }

        public string Name => "Stub2D";
        public int Dimensions => 2;
        public LiteDB.Spatial.SpatialIndexOptions Options { get; }
        public LiteDB.Spatial.ISpatialIndexEncoder IndexEncoder { get; }
        public LiteDB.Spatial.ISpatialMapper Mapper { get; }
        public LiteDB.Spatial.ISpatialDistance Distance { get; }

        public LiteDB.Spatial.ISpatialQueryPlan PlanNear(SpatialGeoPoint center, double radius) => throw new NotSupportedException();
        public LiteDB.Spatial.ISpatialQueryPlan PlanNear(SpatialGeoPoint3D center, double radius) => throw new NotSupportedException();
        public LiteDB.Spatial.ISpatialQueryPlan PlanWithin(LiteDB.Spatial.BoundingBox bounds) => throw new NotSupportedException();
    }

    private sealed class Stub2DMapper : LiteDB.Spatial.ISpatialMapper
    {
        private readonly LiteDB.Spatial.ISpatialIndexEncoder _encoder;
        public Stub2DMapper(LiteDB.Spatial.ISpatialIndexEncoder encoder)
        {
            _encoder = encoder;
        }

        public LiteDB.Spatial.BoundingBox GetBoundingBox(SpatialGeoPoint point)
        {
            return LiteDB.Spatial.BoundingBox.From2D(point.Longitude, point.Latitude, point.Longitude, point.Latitude);
        }

        public LiteDB.Spatial.BoundingBox GetBoundingBox(SpatialGeoPoint3D point)
        {
            throw new NotSupportedException();
        }

        public ulong Encode(SpatialGeoPoint point)
        {
            Span<double> coordinates = stackalloc double[2];
            coordinates[0] = point.Longitude;
            coordinates[1] = point.Latitude;
            return _encoder.Encode(coordinates);
        }

        public ulong Encode(SpatialGeoPoint3D point)
        {
            throw new NotSupportedException();
        }

        public bool TryReadPoint(BaseLiteDB.BsonDocument document, out SpatialGeoPoint point)
        {
            if (document.TryGetValue("location", out var value) && value.IsDocument)
            {
                var location = value.AsDocument;
                point = new SpatialGeoPoint(location["x"].AsDouble, location["y"].AsDouble);
                return true;
            }

            point = default;
            return false;
        }

        public bool TryReadPoint(BaseLiteDB.BsonDocument document, out SpatialGeoPoint3D point)
        {
            point = default;
            return false;
        }
    }

    private sealed class StubDistance : ISpatialDistance
    {
        public double Distance(SpatialGeoPoint left, SpatialGeoPoint right) => 0d;
        public double Distance(SpatialGeoPoint3D left, SpatialGeoPoint3D right) => 0d;
    }
}
