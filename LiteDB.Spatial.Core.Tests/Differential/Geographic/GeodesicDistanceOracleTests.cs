using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Testing.Oracles;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Differential.Geographic;

[Category("oracle")]
public sealed class GeodesicDistanceOracleTests
{
    private const string FixturePath = "fixtures/geodesic_pairs.json";

    [Fact]
    public void HaversineMatchesGeographicLibWithinTolerance()
    {
        var pairs = LoadPairs();
        var distance = new GeographicDistance(GeographicDistanceMode.Haversine);
        var failing = new List<string>();

        foreach (var pair in pairs)
        {
            var expected = GeographicLibOracle.Distance(pair.From.Lon, pair.From.Lat, pair.To.Lon, pair.To.Lat, ellipsoidal: false);
            var actual = distance.Distance(pair.From.ToPoint(), pair.To.ToPoint());
            var tolerance = 1e-4 * expected + 0.05;

            if (Math.Abs(expected - actual) > tolerance)
            {
                failing.Add($"{pair.Id} (expected {expected:F4}, actual {actual:F4})");
            }
        }

        failing.Should().BeEmpty($"Haversine deviations exceeded tolerance for: {string.Join(", ", failing)}");
    }

    [Fact]
    public void VincentyMatchesGeographicLibWithinTolerance()
    {
        var pairs = LoadPairs();
        var distance = new GeographicDistance(GeographicDistanceMode.Vincenty);
        var failing = new List<string>();

        foreach (var pair in pairs)
        {
            var expected = GeographicLibOracle.Distance(pair.From.Lon, pair.From.Lat, pair.To.Lon, pair.To.Lat);
            var actual = distance.Distance(pair.From.ToPoint(), pair.To.ToPoint());
            var tolerance = 1e-4 * expected + 0.05;

            if (Math.Abs(expected - actual) > tolerance)
            {
                failing.Add($"{pair.Id} (expected {expected:F4}, actual {actual:F4})");
            }
        }

        failing.Should().BeEmpty($"Vincenty deviations exceeded tolerance for: {string.Join(", ", failing)}");
    }

    private static IReadOnlyList<GeoPair> LoadPairs()
    {
        using var stream = File.OpenRead(FixturePath);
        return JsonSerializer.Deserialize<List<GeoPair>>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"Fixture '{FixturePath}' could not be loaded.");
    }

    private sealed record GeoPair(string Id, GeoCoordinate From, GeoCoordinate To);

    private sealed record GeoCoordinate(double Lon, double Lat)
    {
        public GeoPoint ToPoint() => new(Lon, Lat);
    }
}
