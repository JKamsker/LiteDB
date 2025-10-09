#nullable enable

using System.Linq;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Indexing;

public sealed class LocalityTests
{
    [Fact]
    public void MortonEncoderMatchesFixturesAndPreservesLocality()
    {
        var fixtures = MortonLocalityFixtures.Load();
        fixtures.Cases.Should().NotBeEmpty();

        foreach (var @case in fixtures.Cases)
        {
            var encoder = new MortonIndexEncoder(@case.Dimensions, @case.PrecisionBits);
            var points = @case.GeneratePoints();
            var expectedCodes = @case.GetExpectedCodes();

            var actualCodes = points
                .Select(point => encoder.Encode(point))
                .ToArray();

            actualCodes.Should()
                .HaveSameCount(expectedCodes, $"fixture '{@case.Name}' defines {expectedCodes.Count} Morton codes")
                .And.Equal(expectedCodes, $"fixture '{@case.Name}' captures the expected Morton ordering");

            var report = LocalityMetrics.EvaluateLocality(@case, points, actualCodes);
            LocalityMetrics.AssertMeetsTargets(@case, report);
        }
    }
}
