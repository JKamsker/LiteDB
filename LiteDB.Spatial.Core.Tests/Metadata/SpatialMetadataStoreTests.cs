extern alias LiteDbBase;

using System.IO;
using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Metadata;

public sealed class SpatialMetadataStoreTests
{
    [Fact]
    public void SaveAndLoadDescriptorRoundTrips()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var store = new LiteDB.Spatial.SpatialMetadataStore(database);
        var options = new LiteDB.Spatial.SpatialIndexOptions(precisionBits: 12);
        var settings = LiteDB.Spatial.SpatialEngineSettings.Create(LiteDB.Spatial.BoundingBox.From2D(-10, -5, 10, 5), LiteDB.Spatial.GeographicDistanceMode.Vincenty);
        var descriptor = new LiteDB.Spatial.SpatialCollectionDescriptor("places", "Geographic", 2, "location", options, settings);

        store.SaveDescriptor("places", descriptor);

        var loaded = store.GetRequiredDescriptor("places");
        loaded.Should().Be(descriptor);
        loaded.Settings.DistanceMode.Should().Be(LiteDB.Spatial.GeographicDistanceMode.Vincenty);
        loaded.Settings.Domain.Should().Be(LiteDB.Spatial.BoundingBox.From2D(-10, -5, 10, 5));
    }

    [Fact]
    public void ValidateDocumentThrowsWhenBoundingBoxDoesNotMatchDimensions()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var store = new LiteDB.Spatial.SpatialMetadataStore(database);
        var descriptor = new LiteDB.Spatial.SpatialCollectionDescriptor("points", "Cartesian3D", 3, "location", new LiteDB.Spatial.SpatialIndexOptions());

        var document = new BaseLiteDB.BsonDocument
        {
            ["_id"] = 1,
            [descriptor.Options.BoundingBoxFieldName] = new BaseLiteDB.BsonArray(new[] { new BaseLiteDB.BsonValue(0), new BaseLiteDB.BsonValue(0), new BaseLiteDB.BsonValue(1), new BaseLiteDB.BsonValue(1) })
        };

        store.SaveDescriptor("points", descriptor);

        var action = () => store.ValidateDocument(document, descriptor);
        action.Should().Throw<LiteDB.Spatial.SpatialMetadataException>()
            .WithMessage("*contains 4 values*");
    }

    [Fact]
    public void GetRequiredDescriptorProvidesFriendlyMessageWhenMissing()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var store = new LiteDB.Spatial.SpatialMetadataStore(database);

        var action = () => store.GetRequiredDescriptor("missing", "Call Spatial.UseGeographic(collection) first.");
        action.Should().Throw<LiteDB.Spatial.SpatialMetadataException>()
            .WithMessage("*Spatial.UseGeographic*");
    }
}
