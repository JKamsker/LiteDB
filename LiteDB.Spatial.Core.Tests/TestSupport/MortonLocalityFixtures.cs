#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal sealed class MortonLocalityFixtures
{
    public IReadOnlyList<MortonLocalityCase> Cases { get; init; } = Array.Empty<MortonLocalityCase>();

    public static MortonLocalityFixtures Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Indexing", "Fixtures", "morton-locality-fixtures.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Locality fixture '{path}' could not be found.");
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        using var stream = File.OpenRead(path);
        var fixtures = JsonSerializer.Deserialize<MortonLocalityFixtures>(stream, options);
        return fixtures ?? throw new InvalidOperationException("Failed to deserialize Morton locality fixtures.");
    }
}

internal sealed class MortonLocalityCase
{
    public string Name { get; init; } = string.Empty;
    public int Dimensions { get; init; }
    public int PrecisionBits { get; init; }
    public int[] GridShape { get; init; } = Array.Empty<int>();
    public int NeighborCount { get; init; }
    public int MortonWindowRadius { get; init; }
    public double TargetAverageOverlap { get; init; }
    public double TargetMinimumOverlap { get; init; }
    public double[] QueryCenter { get; init; } = Array.Empty<double>();
    public double QueryRadius { get; init; }
    public string[] ExpectedCodes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<double[]> GeneratePoints()
    {
        if (GridShape.Length != Dimensions)
        {
            throw new InvalidOperationException($"Fixture '{Name}' defines a grid with {GridShape.Length} dimensions but expects {Dimensions}.");
        }

        var steps = GridShape.Select((count, index) =>
        {
            if (count <= 0)
            {
                throw new InvalidOperationException($"Fixture '{Name}' has a non-positive grid size for axis {index}.");
            }

            if (count == 1)
            {
                return new[] { 0d };
            }

            var axisValues = new double[count];
            for (var i = 0; i < count; i++)
            {
                axisValues[i] = i / (double)(count - 1);
            }

            return axisValues;
        }).ToArray();

        var points = new List<double[]>();
        EnumeratePoints(steps, 0, new double[Dimensions], points);
        return points;
    }

    public IReadOnlyList<ulong> GetExpectedCodes()
    {
        if (ExpectedCodes.Length == 0)
        {
            throw new InvalidOperationException($"Fixture '{Name}' is missing expected Morton codes.");
        }

        var parsed = new List<ulong>(ExpectedCodes.Length);
        foreach (var value in ExpectedCodes)
        {
            if (!ulong.TryParse(value, out var parsedValue))
            {
                throw new InvalidOperationException($"Fixture '{Name}' contains an invalid Morton code value '{value}'.");
            }

            parsed.Add(parsedValue);
        }

        return parsed;
    }

    private void EnumeratePoints(double[][] steps, int axis, double[] current, List<double[]> buffer)
    {
        if (axis == Dimensions)
        {
            buffer.Add((double[])current.Clone());
            return;
        }

        foreach (var value in steps[axis])
        {
            current[axis] = value;
            EnumeratePoints(steps, axis + 1, current, buffer);
        }
    }
}
