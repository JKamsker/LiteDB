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
    public void EnsureIndex_OnGeoPoint_RegistersSpatialDescriptorAndBackingIndexes()
    {
        using var database = CreateDatabase();

        var collection = database.GetCollection<GeoDocument>("points");
        collection.Insert(new GeoDocument { Id = 1, Location = new GeoPoint(10.0, 20.0) });

        collection.EnsureIndex(x => x.Location);

        var metadata = database.GetCollection(SpatialMetadataStore.MetadataCollectionName);
        var descriptors = metadata.FindAll().ToList();
        descriptors.Should().ContainSingle(doc => doc["collection"].AsString.Equals("points", StringComparison.OrdinalIgnoreCase));
        var descriptor = descriptors[0];
        descriptor["engine"].AsString.Should().Be(GeographicEngine.EngineName);
        descriptor["geometryField"].AsString.Should().Be("Location");
        descriptor["options"]["indexFieldName"].AsString.Should().NotBeNullOrWhiteSpace();

        collection.DropIndex("idx")
            .Should().BeTrue("spatial provisioning should create the numeric Morton index");
        collection.DropIndex("mbb")
            .Should().BeTrue("spatial provisioning should create the bounding-box index");
    }

    [Fact]
    public void EnsureIndex_RecreatesBackingIndexes_WhenDropped()
    {
        using var database = CreateDatabase();

        var collection = database.GetCollection<GeoDocument>("points");
        collection.Insert(new GeoDocument { Id = 1, Location = new GeoPoint(42.0, 20.0) });

        collection.EnsureIndex(x => x.Location);

        const string mortonIndexName = "idx";
        const string boundingIndexName = "mbb";

        collection.DropIndex(mortonIndexName)
            .Should().BeTrue("dropping the numeric Morton index should succeed after spatial provisioning");
        collection.DropIndex(boundingIndexName)
            .Should().BeTrue("dropping the bounding-box index should succeed after spatial provisioning");

        collection.EnsureIndex(x => x.Location);

        collection.DropIndex(mortonIndexName)
            .Should().BeTrue("spatial provisioning should recreate the numeric Morton index when missing");
        collection.DropIndex(boundingIndexName)
            .Should().BeTrue("spatial provisioning should recreate the bounding-box index when missing");
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
    public void WhereNear_LinqPredicate_UsesSpatialPlanningRule()
    {
        using var database = CreateDatabase();
        var collection = database.GetCollection<GeoDocument>("points");

        collection.Insert(new GeoDocument { Id = 1, Location = new GeoPoint(0, 0) });
        collection.Insert(new GeoDocument { Id = 2, Location = new GeoPoint(1, 1) });
        collection.EnsureIndex(x => x.Location);

        var plan = collection.Query()
            .WhereNear(x => x.Location, new GeoPoint(0, 0), 1_000)
            .GetPlan();

        plan["index"]["name"].AsString.Should().Be("idx");
        plan["index"]["expr"].AsString.Should().Be("$._idx");
        plan["index"]["mode"].AsString.Should().Contain("SpatialMultiRangeIndex");
    }

    [Fact]
    public void WhereNear_StringPredicate_UsesSpatialPlanningRule()
    {
        using var database = CreateDatabase();
        var collection = database.GetCollection<GeoDocument>("points");

        collection.Insert(new GeoDocument { Id = 1, Location = new GeoPoint(0, 0) });
        collection.Insert(new GeoDocument { Id = 2, Location = new GeoPoint(1, 1) });
        collection.EnsureIndex(x => x.Location);

        var plan = collection.Query()
            .WhereNear("Location", new GeoPoint(0, 0), 1_000)
            .GetPlan();

        plan["index"]["mode"].AsString.Should().Contain("SpatialMultiRangeIndex");
    }

    [Fact]
    public void WhereWithinBox_BsonExpressionPredicate_UsesSpatialPlanningRule()
    {
        using var database = CreateDatabase();
        var collection = database.GetCollection<GeoDocument>("points");

        collection.Insert(new GeoDocument { Id = 1, Location = new GeoPoint(0, 0) });
        collection.Insert(new GeoDocument { Id = 2, Location = new GeoPoint(5, 5) });
        collection.EnsureIndex(x => x.Location);

        var queryable = collection.Query();
        var expressionRegistry = ((BaseLiteDB.LiteQueryable<GeoDocument>)queryable).ExpressionRegistry;

        var plan = queryable
            .WhereWithinBox(BaseLiteDB.BsonExpression.Create("$.Location", expressionRegistry), BoundingBox.From2D(-1, -1, 1, 1))
            .GetPlan();

        plan["index"]["mode"].AsString.Should().Contain("SpatialMultiRangeIndex");
    }

    [Fact]
    public void EnsureIndex_InSharedMode_UsesSpatialPlanningRule()
    {
        var filename = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db");
        var extension = Path.GetExtension(filename);
        var baseName = Path.GetFileNameWithoutExtension(filename);
        var folder = Path.GetDirectoryName(filename);
        var logFile = Path.Combine(folder!, baseName + "-log" + extension);
        var tmpFile = Path.Combine(folder!, baseName + "-tmp" + extension);

        try
        {
            LiteDbPlugins.ILitePlugin[] plugins = { new SpatialPlugin() };
            using var database = new BaseLiteDB.LiteDatabase($"Filename={filename};Connection=Shared", mapper: null, plugins: plugins);
            var collection = database.GetCollection<GeoDocument>("points");

            collection.Insert(new GeoDocument { Id = 1, Location = new GeoPoint(0, 0) });
            collection.Insert(new GeoDocument { Id = 2, Location = new GeoPoint(1, 1) });
            collection.EnsureIndex(x => x.Location);

            var plan = collection.Query()
                .WhereNear(x => x.Location, new GeoPoint(0, 0), 1_000)
                .GetPlan();

            plan["index"]["mode"].AsString.Should().Contain("SpatialMultiRangeIndex");
        }
        finally
        {
            TryDelete(filename);
            TryDelete(logFile);
            TryDelete(tmpFile);
        }
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

    private static void TryDelete(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return;
        }

        try
        {
            if (File.Exists(filename))
            {
                File.Delete(filename);
            }
        }
        catch
        {
        }
    }

    private sealed class GeoDocument
    {
        public int Id { get; set; }

        public GeoPoint Location { get; set; }
    }
}
