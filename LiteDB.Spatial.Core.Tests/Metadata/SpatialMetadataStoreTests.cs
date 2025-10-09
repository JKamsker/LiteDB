extern alias litedb;
using System;
using System.IO;
using FluentAssertions;
using LiteDbRuntime = litedb::LiteDB;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Metadata;

public class SpatialMetadataStoreTests
{
    [Fact]
    public void PersistAndLoadDescriptor_ShouldRoundTrip()
    {
        using var database = new LiteDbRuntime.LiteDatabase(new MemoryStream());
        var options = new SpatialIndexOptions(precisionBits: 18, maxCoveringCells: 32, distanceTolerance: 0.01, indexFieldName: "idx", boundingBoxFieldName: "bbox");
        var descriptor = new SpatialCollectionDescriptor("TestEngine", 2, options);

        SpatialMetadataStore.Persist(database, "places", descriptor);
        var loaded = SpatialMetadataStore.GetRequired(database, "places");

        loaded.Should().NotBeNull();
        loaded.EngineName.Should().Be(descriptor.EngineName);
        loaded.Dimensions.Should().Be(descriptor.Dimensions);
        loaded.Options.Should().Be(descriptor.Options);
    }

    [Fact]
    public void GetRequired_WhenMissing_ShouldSurfaceHelpfulError()
    {
        using var database = new LiteDbRuntime.LiteDatabase(new MemoryStream());

        Action act = () => SpatialMetadataStore.GetRequired(database, "missing");

        act.Should().Throw<LiteDbRuntime.LiteException>()
            .Which.Message.Should().Contain("UseGeographic");
    }

    [Fact]
    public void ValidateBoundingBoxValue_ShouldRejectIncorrectLength()
    {
        var descriptor = new SpatialCollectionDescriptor("Engine", 3, new SpatialIndexOptions());
        var invalidArray = new LiteDbRuntime.BsonArray { 0, 1, 2, 3 };

        Action act = () => descriptor.ValidateBoundingBoxValue(invalidArray, "field");

        act.Should().Throw<LiteDbRuntime.LiteException>()
            .Which.Message.Should().Contain("Expected 6 values");
    }
}
