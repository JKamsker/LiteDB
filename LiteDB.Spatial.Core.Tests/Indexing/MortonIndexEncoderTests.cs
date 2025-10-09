using System;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Indexing;

public sealed class MortonIndexEncoderTests
{
    [Fact]
    public void Encode2DProducesExpectedMortonCodes()
    {
        var encoder = new LiteDB.Spatial.MortonIndexEncoder(2, precisionBits: 2);

        encoder.Encode(stackalloc double[] { 0d, 0d }).Should().Be(0UL);
        encoder.Encode(stackalloc double[] { 1d, 0d }).Should().Be(5UL);
        encoder.Encode(stackalloc double[] { 0d, 1d }).Should().Be(10UL);
        encoder.Encode(stackalloc double[] { 1d, 1d }).Should().Be(15UL);
    }

    [Fact]
    public void Encode3DProducesExpectedMortonCodes()
    {
        var encoder = new LiteDB.Spatial.MortonIndexEncoder(3, precisionBits: 1);

        encoder.Encode(stackalloc double[] { 0d, 0d, 0d }).Should().Be(0UL);
        encoder.Encode(stackalloc double[] { 1d, 0d, 0d }).Should().Be(1UL);
        encoder.Encode(stackalloc double[] { 0d, 1d, 0d }).Should().Be(2UL);
        encoder.Encode(stackalloc double[] { 0d, 0d, 1d }).Should().Be(4UL);
        encoder.Encode(stackalloc double[] { 1d, 1d, 1d }).Should().Be(7UL);
    }

    [Fact]
    public void EncodeRejectsNaN()
    {
        var encoder = new LiteDB.Spatial.MortonIndexEncoder(2, precisionBits: 4);

        var action = () => encoder.Encode(stackalloc double[] { double.NaN, 0d });
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CoverEnumeratesCellsWithinBounds()
    {
        var encoder = new LiteDB.Spatial.MortonIndexEncoder(2, precisionBits: 3);
        var bounds = LiteDB.Spatial.BoundingBox.From2D(0.25, 0.25, 0.5, 0.5);

        var ranges = encoder.Cover(bounds, maxCells: 16);
        ranges.Should().NotBeEmpty();

        var codes = ranges.SelectMany(range => Enumerable.Range(0, (int)(range.End - range.Start + 1)).Select(offset => range.Start + (ulong)offset));
        var lowerBound = encoder.Encode(stackalloc double[] { 0.25, 0.25 });
        var upperBound = encoder.Encode(stackalloc double[] { 0.5, 0.5 });

        foreach (var code in codes)
        {
            code.Should().BeGreaterOrEqualTo(lowerBound);
            code.Should().BeLessOrEqualTo(upperBound);
        }
    }

    [Fact]
    public void UnionAdjacentRangesMergesOverlaps()
    {
        var input = new[]
        {
            new LiteDB.Spatial.SpatialIndexRange(1, 3),
            new LiteDB.Spatial.SpatialIndexRange(4, 6),
            new LiteDB.Spatial.SpatialIndexRange(10, 12)
        };

        var merged = LiteDB.Spatial.MortonIndexEncoder.UnionAdjacentRanges(input);
        merged.Should().HaveCount(2);
        merged[0].Should().Be(new LiteDB.Spatial.SpatialIndexRange(1, 6));
        merged[1].Should().Be(new LiteDB.Spatial.SpatialIndexRange(10, 12));
    }
}
