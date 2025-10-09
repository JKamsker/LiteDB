using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Indexing;

public sealed class LocalityTests
{
    public static IEnumerable<object[]> FixtureData =>
        TestFixtureLoader.LoadLocalityFixtures().Select(fixture => new object[] { fixture });

    [Theory]
    [MemberData(nameof(FixtureData))]
    public void MortonEncoderMaintainsLocality(LocalityFixture fixture)
    {
        var encoder = new MortonIndexEncoder(fixture.Dimensions, fixture.PrecisionBits);
        var points = LocalityTestHelpers.GenerateGrid(fixture.Dimensions, fixture.GridSize);

        var actualCodes = new List<ulong>(points.Length);
        foreach (var point in points)
        {
            actualCodes.Add(encoder.Encode(point));
        }

        LocalityAssertions.AssertFixtureMatch(
            fixture,
            actualCodes,
            LocalityTestHelpers.ComputeMetrics(points, actualCodes, fixture.NeighborCount));
    }
}
