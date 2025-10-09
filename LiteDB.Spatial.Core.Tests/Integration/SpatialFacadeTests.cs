extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Integration;

public sealed class SpatialFacadeTests
{
    [Fact]
    public void GeographicFacadeEnsuresIndexAndPlansQueries()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var metadata = new SpatialMetadataStore(database);
        var collection = database.GetCollection("places");

        Spatial.UseGeographic(metadata, collection.Name, "location");

        collection.Insert(CreateGeographicDocument(1, 13.405, 52.52));
        collection.Insert(CreateGeographicDocument(2, 13.4, 52.5));
        collection.Insert(CreateGeographicDocument(3, -122.33, 47.61));

        var descriptor = Spatial.EnsurePointIndex(metadata, collection);

        collection.FindById(new BaseLiteDB.BsonValue(1))[descriptor.Options.IndexFieldName].IsNull.Should().BeFalse();
        collection.FindById(new BaseLiteDB.BsonValue(1))[descriptor.Options.BoundingBoxFieldName].AsArray.Count.Should().Be(4);

        var plan = Spatial.Near(descriptor, new GeoPoint(13.405, 52.52), 500);
        plan.EngineName.Should().Be(GeographicEngine.EngineName);
        plan.IndexRanges.Should().NotBeEmpty();

        var boxPlan = Spatial.WithinBoundingBox(descriptor, BoundingBox.From2D(13.39, 52.5, 13.42, 52.53));
        boxPlan.EngineName.Should().Be(GeographicEngine.EngineName);
        boxPlan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void Cartesian2DFacadeCoversIndexingAndPlanning()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var metadata = new SpatialMetadataStore(database);
        var collection = database.GetCollection("points2d");

        Spatial.UseCartesian2D(metadata, collection.Name, "position", BoundingBox.From2D(0, 0, 100, 100));

        collection.Insert(CreateCartesian2DDocument(1, 10, 10));
        collection.Insert(CreateCartesian2DDocument(2, 11, 10));
        collection.Insert(CreateCartesian2DDocument(3, 90, 90));

        var descriptor = Spatial.EnsurePointIndex(metadata, collection);

        collection.FindById(new BaseLiteDB.BsonValue(1))[descriptor.Options.IndexFieldName].IsNull.Should().BeFalse();
        collection.FindById(new BaseLiteDB.BsonValue(1))[descriptor.Options.BoundingBoxFieldName].AsArray.Count.Should().Be(4);

        var plan = Spatial.Near(descriptor, new GeoPoint(10, 10), 5);
        plan.EngineName.Should().Be(Cartesian2DEngine.EngineName);
        plan.IndexRanges.Should().NotBeEmpty();

        var boxPlan = Spatial.WithinBoundingBox(descriptor, BoundingBox.From2D(0, 0, 15, 15));
        boxPlan.EngineName.Should().Be(Cartesian2DEngine.EngineName);
        boxPlan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void Cartesian3DFacadeSupportsNearAndBoundingBox()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var metadata = new SpatialMetadataStore(database);
        var collection = database.GetCollection("points3d");

        Spatial.UseCartesian3D(metadata, collection.Name, "position", BoundingBox.From3D(0, 0, 0, 100, 100, 100));

        collection.Insert(CreateCartesian3DDocument(1, 1, 1, 1));
        collection.Insert(CreateCartesian3DDocument(2, 2, 1, 1));
        collection.Insert(CreateCartesian3DDocument(3, 50, 50, 50));

        var descriptor = Spatial.EnsurePointIndex(metadata, collection);

        collection.FindById(new BaseLiteDB.BsonValue(1))[descriptor.Options.IndexFieldName].IsNull.Should().BeFalse();
        collection.FindById(new BaseLiteDB.BsonValue(1))[descriptor.Options.BoundingBoxFieldName].AsArray.Count.Should().Be(6);

        var plan = Spatial.Near(descriptor, new GeoPoint3D(1, 1, 1), 2);
        plan.EngineName.Should().Be(Cartesian3DEngine.EngineName);
        plan.IndexRanges.Should().NotBeEmpty();

        var boxPlan = Spatial.WithinBoundingBox(descriptor, BoundingBox.From3D(0, 0, 0, 5, 5, 5));
        boxPlan.EngineName.Should().Be(Cartesian3DEngine.EngineName);
        boxPlan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void NearPlansReduceCandidateSetForCartesian2D()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var metadata = new SpatialMetadataStore(database);
        var collection = database.GetCollection("grid2d");

        Spatial.UseCartesian2D(metadata, collection.Name, "position", BoundingBox.From2D(0, 0, 40, 40));

        var id = 1;
        for (var x = 0; x < 40; x++)
        {
            for (var y = 0; y < 40; y++)
            {
                collection.Insert(CreateCartesian2DDocument(id++, x, y));
            }
        }

        var descriptor = Spatial.EnsurePointIndex(metadata, collection);
        var center = new GeoPoint(10, 10);
        var radius = 3d;
        var plan = Spatial.Near(descriptor, center, radius);

        var result = ExecutePlan(descriptor, collection, plan, center, radius);

        result.CandidateCount.Should().BeLessThan(id - 1);
        result.MatchCount.Should().BeGreaterThan(0);
    }

    private static BaseLiteDB.BsonDocument CreateGeographicDocument(int id, double longitude, double latitude)
    {
        return new BaseLiteDB.BsonDocument
        {
            ["_id"] = id,
            ["location"] = new BaseLiteDB.BsonDocument
            {
                ["longitude"] = longitude,
                ["latitude"] = latitude
            }
        };
    }

    private static BaseLiteDB.BsonDocument CreateCartesian2DDocument(int id, double x, double y)
    {
        return new BaseLiteDB.BsonDocument
        {
            ["_id"] = id,
            ["position"] = new BaseLiteDB.BsonDocument
            {
                ["x"] = x,
                ["y"] = y
            }
        };
    }

    private static BaseLiteDB.BsonDocument CreateCartesian3DDocument(int id, double x, double y, double z)
    {
        return new BaseLiteDB.BsonDocument
        {
            ["_id"] = id,
            ["position"] = new BaseLiteDB.BsonDocument
            {
                ["x"] = x,
                ["y"] = y,
                ["z"] = z
            }
        };
    }

    private static (int CandidateCount, int MatchCount) ExecutePlan(
        SpatialCollectionDescriptor descriptor,
        BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection,
        ISpatialQueryPlan plan,
        GeoPoint center,
        double radius)
    {
        var engine = descriptor.Engine ?? throw new InvalidOperationException("Descriptor must expose a runtime engine.");
        var options = descriptor.Options;
        var candidates = new HashSet<int>();
        var matches = new HashSet<int>();

        foreach (var range in plan.IndexRanges)
        {
            var start = ToIndexValue(range.Start);
            var end = ToIndexValue(range.End);
            var query = collection.Query().Where(BaseLiteDB.Query.Between(options.IndexFieldName, start, end));

            foreach (var document in query.ToDocuments())
            {
                var id = document["_id"].AsInt32;
                if (!candidates.Add(id))
                {
                    continue;
                }

                if (!engine.Mapper.TryReadPoint(document, out GeoPoint point2D))
                {
                    continue;
                }

                if (plan.CoveringBounds.HasValue && !Contains(plan.CoveringBounds.Value, point2D))
                {
                    continue;
                }

                var distance = engine.Distance.Distance(center, point2D);
                if (distance <= radius + descriptor.Options.DistanceTolerance)
                {
                    matches.Add(id);
                }
            }
        }

        return (candidates.Count, matches.Count);
    }

    private static BaseLiteDB.BsonValue ToIndexValue(ulong value)
    {
        if (value <= long.MaxValue)
        {
            return new BaseLiteDB.BsonValue((long)value);
        }

        return new BaseLiteDB.BsonValue((decimal)value);
    }

    private static bool Contains(BoundingBox box, GeoPoint point)
    {
        var values = box.GetValues();
        return point.Longitude >= values[0]
            && point.Latitude >= values[1]
            && point.Longitude <= values[2]
            && point.Latitude <= values[3];
    }
}
