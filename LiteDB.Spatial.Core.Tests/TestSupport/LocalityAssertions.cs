using System.Collections.Generic;
using FluentAssertions;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

public static class LocalityAssertions
{
    public static void AssertFixtureMatch(LocalityFixture fixture, IReadOnlyList<ulong> actualCodes, LocalityMetrics metrics)
    {
        actualCodes.Should().BeEquivalentTo(fixture.MortonCodes, options => options.WithStrictOrdering());

        metrics.AverageOverlap.Should()
            .BeGreaterOrEqualTo(fixture.ExpectedOverlap - 0.01, "Morton encoding locality should not regress");

        metrics.AverageSpan.Should()
            .BeLessOrEqualTo(fixture.AverageWindowSpan * 1.05, "Morton ordering should keep the average window span close to the recorded baseline");

        metrics.WorstCaseSpan.Should()
            .BeLessOrEqualTo(fixture.AverageWindowSpan * 2, "worst case window should stay bounded compared to the recorded fixture");
    }
}
