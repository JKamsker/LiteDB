using System;
using FluentAssertions;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Indexing;

public sealed class MortonIndexEncoderTests
{
    [Fact]
    public void Encode_ShouldInterleaveBitsFor2DCoordinates()
    {
        var encoder = new MortonIndexEncoder(dimensions: 2, precisionBits: 3);

        encoder.Encode(new[] { 0d, 0d }).Should().Be(0);
        encoder.Encode(new[] { 1d, 1d }).Should().Be(63);
        encoder.Encode(new[] { 0.5, 0.5 }).Should().Be(48);
    }

    [Fact]
    public void Encode_ShouldInterleaveBitsFor3DCoordinates()
    {
        var encoder = new MortonIndexEncoder(dimensions: 3, precisionBits: 2);

        encoder.Encode(new[] { 0d, 0d, 0d }).Should().Be(0);
        encoder.Encode(new[] { 1d, 1d, 1d }).Should().Be(63);
        encoder.Encode(new[] { 0.5, 0.25, 0.75 }).Should().Be(0b101010);
    }

    [Fact]
    public void Encode_ShouldClampAndRejectInvalidValues()
    {
        var encoder = new MortonIndexEncoder(2, precisionBits: 3);

        encoder.Encode(new[] { -1d, 2d }).Should().Be(0b101010);

        Action action = () => encoder.Encode(new[] { double.NaN, 0d });
        action.Should().Throw<ArgumentException>().WithMessage("*finite*");
    }

    [Fact]
    public void Cover_ShouldRespectMaxCells()
    {
        var encoder = new MortonIndexEncoder(2, precisionBits: 3);
        var bounds = BoundingBox.From2D(0d, 0d, 1d, 1d);

        var ranges = encoder.Cover(bounds, maxCells: 4);

        ranges.Should().HaveCountLessOrEqualTo(4);
        ranges.Should().BeInAscendingOrder(r => r.Start);
        ranges.Should().OnlyContain(range => range.End >= range.Start);
    }

    [Fact]
    public void Cover_ShouldProduceSingleRangeForPoint()
    {
        var encoder = new MortonIndexEncoder(2, precisionBits: 3);
        var bounds = BoundingBox.From2D(0.5, 0.5, 0.5, 0.5);

        var ranges = encoder.Cover(bounds, maxCells: 8);

        ranges.Should().HaveCount(1);
        var encoded = encoder.Encode(new[] { 0.5, 0.5 });
        ranges[0].Start.Should().Be(encoded);
        ranges[0].End.Should().Be(encoded);
    }

    [Fact]
    public void MergeAdjacentRanges_ShouldCoalesceTouchingRanges()
    {
        var input = new[]
        {
            new SpatialIndexRange(1, 2),
            new SpatialIndexRange(3, 5),
            new SpatialIndexRange(10, 12)
        };

        var merged = MortonIndexEncoder.MergeAdjacentRanges(input);

        merged.Should().Equal(new SpatialIndexRange(1, 5), new SpatialIndexRange(10, 12));
    }
}
