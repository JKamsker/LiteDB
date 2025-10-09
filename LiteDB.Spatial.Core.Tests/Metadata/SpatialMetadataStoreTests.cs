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
        var descriptor = new LiteDB.Spatial.SpatialCollectionDescriptor("Geographic", 2, new LiteDB.Spatial.SpatialIndexOptions(precisionBits: 12));

        store.SaveDescriptor("places", descriptor);

        var loaded = store.GetRequiredDescriptor("places");
        loaded.Should().Be(descriptor);
    }

    [Fact]
    public void ValidateDocumentThrowsWhenBoundingBoxDoesNotMatchDimensions()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var store = new LiteDB.Spatial.SpatialMetadataStore(database);
        var descriptor = new LiteDB.Spatial.SpatialCollectionDescriptor("Cartesian3D", 3, new LiteDB.Spatial.SpatialIndexOptions());

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
