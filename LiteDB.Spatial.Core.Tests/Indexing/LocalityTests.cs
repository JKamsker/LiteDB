using System.Linq;
using FluentAssertions;
using LiteDB.Spatial.Core.Tests.Support;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Indexing;

public sealed class LocalityTests
{
    [Theory]
    [InlineData("uniform_2d_precision8")]
    [InlineData("uniform_3d_precision6")]
    public void MortonEncoderPreservesLocalityAcrossFixtures(string fixtureId)
    {
        var fixture = LocalityFixture.Load(fixtureId);
        var encoder = new MortonIndexEncoder(fixture.Dimensions, fixture.PrecisionBits);

        var metrics = fixture.Evaluate(encoder);
        metrics.ShouldMatchFixture();

        var uniqueCodes = metrics.ComputedCodes.ToHashSet();
        uniqueCodes.Count.Should().Be(metrics.ComputedCodes.Count, "fixtures should not introduce duplicate Morton codes");
        metrics.Overlap.MortonNeighborhoodSizes.Should().OnlyContain(size => size == metrics.Overlap.EffectiveTopK, "Morton ordering should yield a consistent neighbour window");
    }
}
