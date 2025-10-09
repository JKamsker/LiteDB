extern alias litedb;

using System;
using System.IO;
using FluentAssertions;
using litedb::LiteDB;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Metadata;

public sealed class SpatialMetadataStoreTests
{
    [Fact]
    public void RoundTrip_ShouldPersistDescriptor()
    {
        using var stream = new MemoryStream();
        using var database = new LiteDatabase(stream);
        var store = new SpatialMetadataStore(database);

        var options = new SpatialIndexOptions(precisionBits: 28, maxCoveringCells: 32, distanceTolerance: 0.25, indexFieldName: "_idx", boundingBoxFieldName: "_mbb");
        var descriptor = new SpatialCollectionDescriptor("points", "geographic", 2, "location", options);

        store.UpsertDescriptor(descriptor);

        var loaded = store.TryGetDescriptor("points");

        loaded.Should().NotBeNull();
        loaded.Should().Be(descriptor);
    }

    [Fact]
    public void GetRequiredDescriptor_ShouldThrowWhenMissing()
    {
        using var stream = new MemoryStream();
        using var database = new LiteDatabase(stream);
        var store = new SpatialMetadataStore(database);

        var act = () => store.GetRequiredDescriptor("missing");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*UseGeographic*");
    }

    [Fact]
    public void ValidateDocument_ShouldDetectBoundingBoxMismatch()
    {
        using var stream = new MemoryStream();
        using var database = new LiteDatabase(stream);
        var store = new SpatialMetadataStore(database);
        var options = new SpatialIndexOptions(indexFieldName: "_idx", boundingBoxFieldName: "_mbb");
        var descriptor = new SpatialCollectionDescriptor("points", "cartesian3d", 3, "position", options);

        var document = new BsonDocument
        {
            ["_id"] = 1,
            [descriptor.Options.BoundingBoxFieldName] = new BsonArray { 0d, 0d, 1d, 1d },
            [descriptor.Options.IndexFieldName] = 123L
        };

        var act = () => store.ValidateDocument(descriptor, document);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*declares 3 dimensions*");
    }

    [Fact]
    public void ValidateDocument_ShouldRejectNonNumericIndex()
    {
        using var stream = new MemoryStream();
        using var database = new LiteDatabase(stream);
        var store = new SpatialMetadataStore(database);
        var options = new SpatialIndexOptions(indexFieldName: "_idx", boundingBoxFieldName: "_mbb");
        var descriptor = new SpatialCollectionDescriptor("points", "cartesian2d", 2, "position", options);

        var document = new BsonDocument
        {
            ["_id"] = 1,
            [descriptor.Options.BoundingBoxFieldName] = new BsonArray { 0d, 0d, 1d, 1d },
            [descriptor.Options.IndexFieldName] = "abc"
        };

        var act = () => store.ValidateDocument(descriptor, document);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*64-bit integer*");
    }

    [Fact]
    public void ValidateDocument_ShouldAllowDocumentsWithoutSpatialFields()
    {
        using var stream = new MemoryStream();
        using var database = new LiteDatabase(stream);
        var store = new SpatialMetadataStore(database);
        var options = new SpatialIndexOptions(indexFieldName: "_idx", boundingBoxFieldName: "_mbb");
        var descriptor = new SpatialCollectionDescriptor("points", "cartesian2d", 2, "position", options);

        var document = new BsonDocument { ["_id"] = 1 };

        store.Invoking(x => x.ValidateDocument(descriptor, document)).Should().NotThrow();
    }
}
