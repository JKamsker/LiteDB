using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Indexing;

public sealed class LocalityTests
{
    public static IEnumerable<object[]> FixtureCases => LocalityFixture.Cases.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public void MortonEncoderPreservesRecordedLocality(LocalityFixtureCase fixture)
    {
        var encoder = new MortonIndexEncoder(fixture.Dimensions, fixture.PrecisionBits);
        var grid = LocalityTestSupport.GenerateGrid(fixture.Shape);
        var codes = LocalityTestSupport.EncodePoints(grid, encoder, fixture.Dimensions);

        fixture.AssertMortonCodes(codes);

        var metrics = LocalityTestSupport.ComputeMetrics(
            grid,
            codes,
            fixture.Dimensions,
            fixture.NeighborCount,
            fixture.WindowRadius);

        fixture.AssertLocalityThresholds(metrics);
    }
}
