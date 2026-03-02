extern alias LiteDbBase;

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial.Plugin;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Plugin;

public sealed class SpatialPluginFactoryTests
{
    [Fact]
    public void Factory_handles_can_execute_spatial_queries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            using var factory = new BaseLiteDB.LiteDatabaseBuilder()
                .UseFile(path)
                .UsePlugin(new SpatialPlugin())
                .BuildFactory();

            using (var firstHandle = factory.CreateDatabase())
            {
                var collection = firstHandle.GetCollection<GeoDocument>("points");
                collection.Insert(new GeoDocument { Id = 1, Location = new GeoPoint(0, 0) });

                Spatial.UseGeographic(collection, x => x.Location);
            }

            using (var secondHandle = factory.CreateDatabase())
            {
                var collection = secondHandle.GetCollection<GeoDocument>("points");

                var results = collection.Query()
                    .WhereNear(x => x.Location, new GeoPoint(0, 0), 1_000)
                    .ToList();

                results.Select(x => x.Id).Should().Equal(1);
            }
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class GeoDocument
    {
        public int Id { get; set; }

        public GeoPoint Location { get; set; }
    }
}

