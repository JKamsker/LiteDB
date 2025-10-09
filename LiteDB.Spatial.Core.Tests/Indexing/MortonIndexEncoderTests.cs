using System;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Indexing;

public class MortonIndexEncoderTests
{
    [Fact]
    public void Encode2D_ShouldProduceExpectedOrderingForBinaryGrid()
    {
        var encoder = new MortonIndexEncoder(2, precisionBits: 1);

        encoder.Encode(new[] { 0d, 0d }).Should().Be(0);
        encoder.Encode(new[] { 1d, 0d }).Should().Be(1);
        encoder.Encode(new[] { 0d, 1d }).Should().Be(2);
        encoder.Encode(new[] { 1d, 1d }).Should().Be(3);
    }

    [Fact]
    public void Encode3D_ShouldInterleaveBitsAcrossAllAxes()
    {
        var encoder = new MortonIndexEncoder(3, precisionBits: 1);

        encoder.Encode(new[] { 0d, 0d, 0d }).Should().Be(0);
        encoder.Encode(new[] { 1d, 0d, 0d }).Should().Be(1);
        encoder.Encode(new[] { 0d, 1d, 0d }).Should().Be(2);
        encoder.Encode(new[] { 0d, 0d, 1d }).Should().Be(4);
        encoder.Encode(new[] { 1d, 1d, 1d }).Should().Be(7);
    }

    [Fact]
    public void Encode_ShouldRejectNaNCoordinates()
    {
        var encoder = new MortonIndexEncoder(2, precisionBits: 4);

        Action act = () => encoder.Encode(new[] { double.NaN, 0.5 });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Encode_ShouldClampValuesOutsideNormalizedRange()
    {
        var encoder = new MortonIndexEncoder(2, precisionBits: 4);

        var min = encoder.Encode(new[] { -10d, -5d });
        var max = encoder.Encode(new[] { 10d, 20d });

        min.Should().Be(0);
        max.Should().Be((1UL << 8) - 1UL);
    }

    [Fact]
    public void Cover_ShouldRespectMaxCells()
    {
        var encoder = new MortonIndexEncoder(2, precisionBits: 5);
        var bounds = BoundingBox.From2D(0d, 0d, 0.5, 0.5);

        var ranges = encoder.Cover(bounds, maxCells: 4);

        ranges.Should().NotBeEmpty();
        ranges.Count.Should().BeLessOrEqualTo(4);
        ranges.Should().BeInAscendingOrder(range => range.Start);
    }

    [Fact]
    public void CoalesceRanges_ShouldMergeOverlappingSegments()
    {
        var input = new[]
        {
            new SpatialIndexRange(0, 10),
            new SpatialIndexRange(11, 20),
            new SpatialIndexRange(30, 40)
        };

        var merged = MortonIndexEncoder.CoalesceRanges(input);

        merged.Should().HaveCount(2);
        merged[0].Should().Be(new SpatialIndexRange(0, 20));
        merged[1].Should().Be(new SpatialIndexRange(30, 40));
    }
}
