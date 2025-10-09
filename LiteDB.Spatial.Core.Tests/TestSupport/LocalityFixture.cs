#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LiteDB.Spatial.Core.Tests;

internal static class LocalityFixture
{
    private const string FixtureFileName = "morton-locality-reference.json";

    private static readonly Lazy<IReadOnlyList<LocalityFixtureCase>> _cases = new(LoadCases, isThreadSafe: true);

    public static IReadOnlyList<LocalityFixtureCase> Cases => _cases.Value;

    private static IReadOnlyList<LocalityFixtureCase> LoadCases()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidatePaths = new[]
        {
            Path.Combine(baseDirectory, FixtureFileName),
            Path.Combine(baseDirectory, "Indexing", "Fixtures", FixtureFileName)
        };

        var fixturePath = candidatePaths.FirstOrDefault(File.Exists);

        if (fixturePath is null)
        {
            throw new FileNotFoundException($"Missing locality fixture '{FixtureFileName}'. The test asset should be copied to the output folder.");
        }

        using var stream = File.OpenRead(fixturePath);
        var document = JsonSerializer.Deserialize<LocalityFixtureDocument>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (document?.Cases == null || document.Cases.Count == 0)
        {
            throw new InvalidOperationException("Locality fixture did not contain any cases.");
        }

        return document.Cases
            .Select(c => c.ToCase())
            .ToArray();
    }

    private sealed class LocalityFixtureDocument
    {
        public List<LocalityFixtureEntry>? Cases { get; set; }
    }

    private sealed class LocalityFixtureEntry
    {
        public string Name { get; set; } = string.Empty;

        public int Dimensions { get; set; }

        public int PrecisionBits { get; set; }

        public int[] Shape { get; set; } = Array.Empty<int>();

        public int NeighborCount { get; set; }

        public int WindowRadius { get; set; }

        public ulong[] Codes { get; set; } = Array.Empty<ulong>();

        public LocalityFixtureMetricsEntry Metrics { get; set; } = new();

        public LocalityFixtureCase ToCase()
        {
            if (Shape.Length == 0)
            {
                throw new InvalidOperationException($"Fixture '{Name}' does not define a grid shape.");
            }

            if (Metrics is null)
            {
                throw new InvalidOperationException($"Fixture '{Name}' does not define expected metrics.");
            }

            var metrics = new LocalityFixtureMetrics(
                Metrics.AverageOverlap,
                Metrics.AverageWindow,
                Metrics.MinimumOverlap,
                Metrics.MaximumWindow);

            return new LocalityFixtureCase(
                Name,
                Dimensions,
                PrecisionBits,
                Shape,
                NeighborCount,
                WindowRadius,
                Codes,
                metrics);
        }
    }

    private sealed class LocalityFixtureMetricsEntry
    {
        public double AverageOverlap { get; set; }

        public double AverageWindow { get; set; }

        public double MinimumOverlap { get; set; }

        public double MaximumWindow { get; set; }
    }
}

public sealed record LocalityFixtureCase(
    string Name,
    int Dimensions,
    int PrecisionBits,
    IReadOnlyList<int> Shape,
    int NeighborCount,
    int WindowRadius,
    IReadOnlyList<ulong> Codes,
    LocalityFixtureMetrics Metrics);

public readonly record struct LocalityFixtureMetrics(
    double AverageOverlap,
    double AverageWindow,
    double MinimumOverlap,
    double MaximumWindow);
