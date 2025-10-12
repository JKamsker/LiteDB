using System;
using System.Collections.Generic;
using FluentAssertions;

namespace LiteDB.Spatial.Core.Tests;

internal static class LocalityAssertionExtensions
{
    public static void AssertMortonCodes(this LocalityFixtureCase fixture, IReadOnlyList<ulong> actual)
    {
        actual.Should().Equal(fixture.Codes, $"Morton codes for '{fixture.Name}' should match the recorded fixture");
    }

    public static void AssertLocalityThresholds(this LocalityFixtureCase fixture, LocalityMetrics metrics)
    {
        metrics.AverageOverlap.Should().BeGreaterOrEqualTo(
            fixture.Metrics.MinimumOverlap,
            $"average overlap for '{fixture.Name}' should remain above the guardrail of {fixture.Metrics.MinimumOverlap:0.###}");

        metrics.AverageWindow.Should().BeLessOrEqualTo(
            fixture.Metrics.MaximumWindow,
            $"Morton window size for '{fixture.Name}' should remain below {fixture.Metrics.MaximumWindow:0.###}");

        metrics.AverageOverlap.Should().BeApproximately(
            fixture.Metrics.AverageOverlap,
            precision: 0.02,
            $"average overlap for '{fixture.Name}' should stay close to the reference value {fixture.Metrics.AverageOverlap:0.###}");

        metrics.AverageWindow.Should().BeApproximately(
            fixture.Metrics.AverageWindow,
            precision: 2.0,
            $"window density for '{fixture.Name}' should stay close to the recorded value {fixture.Metrics.AverageWindow:0.###}");
    }
}
