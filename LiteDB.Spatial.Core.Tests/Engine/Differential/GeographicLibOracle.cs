using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial;

namespace LiteDB.Spatial.Core.Tests.Engine.Differential;

internal static class GeographicLibOracle
{
    private static readonly Lazy<IReadOnlyDictionary<string, GeodesicPair>> Cache = new(() =>
    {
        var dtos = TestDataLoader.LoadCollection<GeodesicPairDto>("geodesic_pairs.json");
        return dtos
            .Select(GeodesicPair.FromDto)
            .ToDictionary(pair => pair.Id, StringComparer.Ordinal);
    });

    public static IReadOnlyList<GeodesicPair> LoadPairs()
    {
        return Cache.Value.Values.ToList();
    }

    public static double Distance(string id, GeoPoint from, GeoPoint to)
    {
        if (!Cache.Value.TryGetValue(id, out var pair))
        {
            throw new KeyNotFoundException($"Geodesic pair '{id}' was not found in the oracle cache.");
        }

        const double coordinateTolerance = 1e-9;
        if (Math.Abs(pair.From.Longitude - from.Longitude) > coordinateTolerance ||
            Math.Abs(pair.From.Latitude - from.Latitude) > coordinateTolerance ||
            Math.Abs(pair.To.Longitude - to.Longitude) > coordinateTolerance ||
            Math.Abs(pair.To.Latitude - to.Latitude) > coordinateTolerance)
        {
            throw new InvalidOperationException(
                $"Fixture '{id}' does not match the requested coordinates. Ensure the test dataset is up to date.");
        }

        return pair.GeographicLibDistanceMeters;
    }
}
