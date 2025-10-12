#nullable enable

using System;
using System.Collections.Generic;

namespace LiteDB.Spatial.Core.Tests.Oracles;

internal static class GeographicLibOracle
{
    private const string FixturePath = "geographic/geodesic_pairs.json";
    private static readonly Lazy<IReadOnlyList<GeodesicPair>> CachedPairs = new(LoadPairs, isThreadSafe: true);

    public static IReadOnlyList<GeodesicPair> Pairs => CachedPairs.Value;

    public static double Distance(GeodesicPair pair)
    {
        if (pair is null)
        {
            throw new ArgumentNullException(nameof(pair));
        }

        return pair.DistanceMeters ?? throw new InvalidOperationException($"Fixture '{pair.Id}' is missing a GeographicLib distance.");
    }

    private static IReadOnlyList<GeodesicPair> LoadPairs()
    {
        var pairs = FixtureLoader.Load<List<GeodesicPair>>(FixturePath);
        return pairs;
    }

    internal sealed class GeodesicPair
    {
        public string Id { get; set; } = string.Empty;

        public double Longitude1 { get; set; }

        public double Latitude1 { get; set; }

        public double Longitude2 { get; set; }

        public double Latitude2 { get; set; }

        public double? DistanceMeters { get; set; }
    }
}
