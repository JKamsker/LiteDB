extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Testing.Oracles;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Differential.Geographic;

[Category("oracle")]
public sealed class GeographicNearPostgisTests
{
    private const string ToggleName = "SPATIAL_DB_TESTS";
    private const string ConnectionStringName = "POSTGIS_CONNECTION_STRING";
    private const string TableName = "litedb_spatial_oracle_points";

    [Fact]
    public async Task NearMatchesPostgisWhenToggleEnabled()
    {
        if (!IsEnabled(out var connectionString))
        {
            return;
        }

        var oracle = new PostgisOracle(connectionString!);
        await oracle.EnsureExtensionAsync();

        var samplePoints = new List<(int Id, double Lon, double Lat)>
        {
            (1, 173.1850, 52.9000),
            (2, -176.6330, 51.8800),
            (3, -165.4060, 64.5011),
            (4, 177.5090, 64.7360),
            (5, 178.0650, -18.1248),
            (6, -171.7514, -13.7590),
            (7, 30.0000, 89.0000)
        };

        await oracle.SeedPointsAsync(TableName, samplePoints);

        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<GeographicPoint>("geo_points");
        var descriptor = Spatial.UseGeographic(collection, x => x.Location, distanceMode: GeographicDistanceMode.Vincenty);
        var documents = samplePoints.Select(p => new GeographicPoint(p.Id, new GeoPoint(p.Lon, p.Lat))).ToList();
        collection.Insert(documents);

        var testCases = new List<(GeoPoint Center, double Radius)>
        {
            (new GeoPoint(173.0, 53.0), 250_000d),
            (new GeoPoint(-172.0, -15.0), 450_000d),
            (new GeoPoint(30.0, 88.5), 150_000d)
        };

        foreach (var (center, radius) in testCases)
        {
            var plan = SpatialGeographic.Near(descriptor, center, radius, GeographicDistanceMode.Vincenty);
            var explain = SpatialDiagnostics.Explain(plan, descriptor);
            var summary = explain.ToString();

            var indexPosition = summary.IndexOf("_idx", StringComparison.Ordinal);
            var predicatePosition = summary.IndexOf("Exact predicate", StringComparison.Ordinal);
            indexPosition.Should().BeGreaterThanOrEqualTo(0, "Explain should include index usage");
            predicatePosition.Should().BeGreaterThanOrEqualTo(0, "Explain should include exact predicate");
            predicatePosition.Should().BeGreaterThan(indexPosition, "Exact predicate should appear after index usage in explain output");

            var liteDbResults = Spatial.Near(collection, x => x.Location, center, radius)
                .Select(point => point.Id)
                .OrderBy(id => id)
                .ToList();

            var oracleResults = (await oracle.QueryDWithinAsync(TableName, center.Longitude, center.Latitude, radius))
                .OrderBy(id => id)
                .ToList();

            liteDbResults.Should().Equal(oracleResults, $"LiteDB results differ from PostGIS for center {center} radius {radius}");
        }
    }

    private static bool IsEnabled(out string? connectionString)
    {
        var toggle = Environment.GetEnvironmentVariable(ToggleName);
        connectionString = Environment.GetEnvironmentVariable(ConnectionStringName);

        if (!string.Equals(toggle, "1", StringComparison.Ordinal))
        {
            connectionString = null;
            return false;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        return true;
    }

    private sealed record GeographicPoint(int Id, GeoPoint Location);
}
