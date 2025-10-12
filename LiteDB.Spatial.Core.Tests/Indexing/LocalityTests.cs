using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace LiteDB.Spatial.Core.Tests.Indexing;

[Category("locality")]
public sealed class LocalityTests
{
    private readonly ITestOutputHelper _output;

    public LocalityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> Fixtures => LocalityTestHelpers
        .LoadDefinitions()
        .Select(definition => new object[] { definition });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void MortonLocalityMatchesRecordedExpectations(LocalityFixtureDefinition definition)
    {
        var grid = LocalityTestHelpers.LoadGridFixture(definition);
        var analysis = LocalityTestHelpers.Analyze(definition, grid);

        _output.WriteLine($"fixture={definition.Id}");
        _output.WriteLine($"points={analysis.PointsByRank.Count}");
        _output.WriteLine($"avgOverlap={analysis.Metrics.AverageOverlap:F6} minOverlap={analysis.Metrics.MinimumOverlap:F6}");
        _output.WriteLine($"avgWindow={analysis.Metrics.AverageWindow:F2} maxWindow={analysis.Metrics.MaximumWindow:F2}");
        _output.WriteLine($"avgSpan={analysis.Metrics.AverageSpan:F2} worstSpan={analysis.Metrics.WorstSpan:F2}");

        LocalityAssertions.AssertLocality(analysis);
    }
}
