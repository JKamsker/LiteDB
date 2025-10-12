extern alias LiteDbBase;

#nullable enable

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using FluentAssertions.Execution;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.Oracles;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Engine;

[Category("oracle")]
public sealed class GeographicNearPostgisTests
{
    [Fact]
    public void NearMatchesPostgisWhenEnabled()
    {
        var fixture = FixtureLoader.Load<NearFixture>("fixtures/geographic_near_points.json");
        var center = new GeoPoint(fixture.Center.Lon, fixture.Center.Lat);
        var radius = fixture.RadiusMeters;

        if (!PostgisOracle.TryDWithin(fixture.Points, center, radius, out var expected, out var skipReason))
        {
            SkipTest(skipReason ?? "PostGIS oracle unavailable.");
            return;
        }

        foreach (var mode in new[] { GeographicDistanceMode.Haversine, GeographicDistanceMode.Vincenty })
        {
            using var scope = new AssertionScope();
            scope.AddReportable("distanceMode", mode.ToString());

            using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
            var places = database.GetCollection<City>("cities");
            foreach (var point in fixture.Points)
            {
                places.Insert(new City
                {
                    Id = point.Id,
                    Name = point.Name,
                    Location = new GeoPoint(point.Lon, point.Lat)
                });
            }

            var descriptor = Spatial.UseGeographic(places, x => x.Location, distanceMode: mode);
            Spatial.EnsurePointIndex(places);

            var plan = SpatialGeographic.Near(descriptor, center, radius, mode);
            var explain = SpatialDiagnostics.Explain(plan, descriptor);
            var summary = explain.ToString();

            summary.Should().Contain("_idx");
            summary.Should().Contain("Exact predicate");
            summary.IndexOf("_idx", StringComparison.Ordinal).Should()
                .BeLessThan(summary.IndexOf("Exact predicate", StringComparison.Ordinal));

            var actual = Spatial.Near(places, x => x.Location, center, radius)
                .Select(x => x.Id)
                .OrderBy(x => x)
                .ToArray();

            actual.Should().BeEquivalentTo(expected, options => options.WithoutStrictOrdering());
        }
    }

    private sealed class City
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public GeoPoint Location { get; set; }
    }

    private static void SkipTest(string message)
    {
        var assembly = typeof(FactAttribute).Assembly;
        var skipType = assembly.GetType("Xunit.Sdk.SkipException") ?? assembly.GetType("Xunit.SkipException");

        if (skipType != null && typeof(Exception).IsAssignableFrom(skipType))
        {
            Exception exception;
            try
            {
                exception = (Exception)Activator.CreateInstance(skipType, message)!;
            }
            catch (MissingMethodException)
            {
                exception = (Exception)Activator.CreateInstance(skipType)!;
            }

            throw exception;
        }

        // Fallback: log the reason and bail out.
        Console.WriteLine($"Skipping PostGIS comparison: {message}");
    }
}
