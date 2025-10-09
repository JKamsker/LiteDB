extern alias litedbmain;

using System;
using System.IO;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;
using LiteDatabase = litedbmain::LiteDB.LiteDatabase;
using LiteDbBsonDocument = litedbmain::LiteDB.BsonDocument;

namespace LiteDB.Spatial.Core.Tests.Metadata;

public class SpatialMetadataStoreTests
{
    [Fact]
    public void SaveAndRetrieve_ShouldRoundTripMetadata()
    {
        using var database = new LiteDatabase(new MemoryStream());
        var store = new SpatialMetadataStore(database);
        var options = new SpatialIndexOptions(precisionBits: 12, maxCoveringCells: 8, distanceTolerance: 0.5, indexFieldName: "idx", boundingBoxFieldName: "box");
        var metadata = new SpatialCollectionMetadata("places", "geographic", 2, options);

        store.Save(metadata);

        var loaded = store.GetOrThrow("places", "UseGeographic(…)");

        loaded.Should().NotBeNull();
        loaded.EngineName.Should().Be("geographic");
        loaded.Dimensions.Should().Be(2);
        loaded.Options.IndexFieldName.Should().Be("idx");
        loaded.Options.BoundingBoxFieldName.Should().Be("box");
    }

    [Fact]
    public void TryGet_ShouldReturnFalseWhenDescriptorMissing()
    {
        using var database = new LiteDatabase(new MemoryStream());
        var store = new SpatialMetadataStore(database);

        store.TryGet("places", out var metadata).Should().BeFalse();
        metadata.Should().BeNull();
    }

    [Fact]
    public void GetOrThrow_ShouldRaiseHelpfulMessageWhenMissing()
    {
        using var database = new LiteDatabase(new MemoryStream());
        var store = new SpatialMetadataStore(database);

        Action act = () => store.GetOrThrow("places", "Spatial.UseGeographic(collection, …)");

        act.Should().Throw<SpatialMetadataException>()
            .Which.Message.Should().Contain("UseGeographic");
    }

    [Fact]
    public void InvalidBoundingBoxLength_ShouldTriggerGuard()
    {
        using var database = new LiteDatabase(new MemoryStream());
        var collection = database.GetCollection("_spatial_meta");

        var document = new LiteDbBsonDocument
        {
            ["_id"] = "places",
            ["collection"] = "places",
            ["engine"] = "cartesian3d",
            ["dimensions"] = 3,
            ["bboxElements"] = 4,
            ["options"] = new LiteDbBsonDocument
            {
                ["precisionBits"] = 12,
                ["maxCoveringCells"] = 8,
                ["distanceTolerance"] = 0.1,
                ["indexField"] = "_idx",
                ["boundingBoxField"] = "_mbb"
            }
        };

        collection.Insert(document);

        var store = new SpatialMetadataStore(database);

        Action act = () => store.TryGet("places", out _);

        act.Should().Throw<SpatialMetadataException>()
            .Which.Message.Should().Contain("bounding box");
    }
}
