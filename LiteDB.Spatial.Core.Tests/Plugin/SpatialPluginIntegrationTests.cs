extern alias LiteDbBase;

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Plugin;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;
using LiteDbPlugins = LiteDbBase::LiteDB.Plugins;

namespace LiteDB.Spatial.Core.Tests.Plugin;

public sealed class SpatialPluginIntegrationTests
{
    [Fact]
    public void EnsureIndex_OnGeoPoint_RegistersSpatialDescriptor()
    {
        using var database = CreateDatabase();

        var collection = database.GetCollection<GeoDocument>("points");
        collection.Insert(new GeoDocument { Id = 1, Location = new GeoPoint(10.0, 20.0) });

        var created = collection.EnsureIndex(x => x.Location);

        created.Should().BeTrue();
        var metadata = database.GetCollection(SpatialMetadataStore.MetadataCollectionName);
        var descriptors = metadata.FindAll().ToList();
        descriptors.Should().ContainSingle(doc => doc["collection"].AsString.Equals("points", StringComparison.OrdinalIgnoreCase));
        var descriptor = descriptors[0];
        descriptor["engine"].AsString.Should().Be(GeographicEngine.EngineName);
        descriptor["geometryField"].AsString.Should().Be("Location");
        descriptor["options"]["indexFieldName"].AsString.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void WhereNear_Expression_SelectsNearbyDocuments()
    {
        using var database = CreateDatabase();
        var collection = database.GetCollection<GeoDocument>("points");

        collection.Insert(new GeoDocument { Id = 1, Location = new GeoPoint(0, 0) });
        collection.Insert(new GeoDocument { Id = 2, Location = new GeoPoint(1, 1) });
        collection.EnsureIndex(x => x.Location);

        var results = collection.Query()
            .WhereNear(x => x.Location, new GeoPoint(0, 0), 1_000) // meters
            .ToList();

        results.Select(x => x.Id).Should().Equal(1);
    }

    [Fact]
    public void LogDiagnostics_ThrowsWhenPluginMissing()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());

        Action act = () => SpatialPlugin.LogDiagnostics(database, throwOnFailure: true);

        act.Should().Throw<BaseLiteDB.LiteException>()
            .Which.Message.Should().Contain("Spatial plugin is not attached");
    }

    [Fact]
    public void LogDiagnostics_ThrowsWhenNoDescriptors()
    {
        using var database = CreateDatabase();

        Action act = () => SpatialPlugin.LogDiagnostics(database, throwOnFailure: true);

        act.Should().Throw<BaseLiteDB.LiteException>()
            .Which.Message.Should().Contain("No spatial descriptors found");
    }

    [Fact]
    public void LogDiagnostics_SucceedsWhenDescriptorsPresent()
    {
        using var database = CreateDatabase();
        var collection = database.GetCollection<GeoDocument>("points");
        collection.Insert(new GeoDocument { Id = 1, Location = new GeoPoint(10, 10) });
        collection.EnsureIndex(x => x.Location);

        Action act = () => SpatialPlugin.LogDiagnostics(database, throwOnFailure: true);

        act.Should().NotThrow();
    }

    private static BaseLiteDB.LiteDatabase CreateDatabase()
    {
        var stream = new MemoryStream();
        LiteDbPlugins.ILitePlugin[] plugins = { new SpatialPlugin() };
        return new BaseLiteDB.LiteDatabase(stream, mapper: null, logStream: null, plugins: plugins);
    }

    private sealed class GeoDocument
    {
        public int Id { get; set; }

        public GeoPoint Location { get; set; }
    }
}
