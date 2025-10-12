using System;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Perf;

public sealed class SpatialPerfHarness
{
    [Fact]
    [Trait("Category", "Perf")]
    public void CompareLiteDbAgainstReferenceEngines()
    {
        if (!PerfTestGuards.IsPerfEnabled())
        {
            return;
        }

        var runner = new SpatialPerfRunner(LocalityFixture.Cases);
        var summary = runner.Execute();

        summary.Engines.Should().NotBeEmpty();

        var liteDb = summary.Engines.First(result => result.Engine == "LiteDB");
        liteDb.Status.Should().Be(PerfEngineStatus.Completed);
        liteDb.Observations.Should().NotBeEmpty("LiteDB runs should produce candidate statistics");
    }
}

internal static class PerfTestGuards
{
    private const string ToggleVariable = "LITEDB_SPATIAL_PERF";

    public static bool IsPerfEnabled()
    {
        var value = Environment.GetEnvironmentVariable(ToggleVariable);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
