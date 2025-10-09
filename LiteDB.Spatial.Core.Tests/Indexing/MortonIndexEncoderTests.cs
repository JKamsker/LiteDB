using System;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Indexing;

public class MortonIndexEncoderTests
{
    [Fact]
    public void Encode2D_ShouldProduceExpectedMortonCodes()
    {
        var encoder = new MortonIndexEncoder(2, precisionBits: 3);

        Encode(encoder, 0.0, 0.0).Should().Be(0UL);
        Encode(encoder, 1.0, 0.0).Should().Be(21UL);
        Encode(encoder, 0.0, 1.0).Should().Be(42UL);
        Encode(encoder, 1.0, 1.0).Should().Be(63UL);
    }

    [Fact]
    public void Encode3D_ShouldInterleaveAllAxes()
    {
        var encoder = new MortonIndexEncoder(3, precisionBits: 2);

        Encode(encoder, 0.0, 0.0, 0.0).Should().Be(0UL);
        Encode(encoder, 1.0, 0.0, 0.0).Should().Be(9UL);
        Encode(encoder, 0.0, 1.0, 0.0).Should().Be(18UL);
        Encode(encoder, 0.0, 0.0, 1.0).Should().Be(36UL);
        Encode(encoder, 1.0, 1.0, 1.0).Should().Be(63UL);
    }

    [Fact]
    public void Encode_ShouldRejectNaN()
    {
        var encoder = new MortonIndexEncoder(2, precisionBits: 2);

        Action act = () => encoder.Encode(new[] { double.NaN, 0.5 });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Cover_ShouldReturnSingleRangeForEntireSpace()
    {
        var encoder = new MortonIndexEncoder(2, precisionBits: 3);
        var bounds = BoundingBox.From2D(0, 0, 1, 1);

        var ranges = encoder.Cover(bounds, maxCells: 4);

        ranges.Should().HaveCount(1);
        ranges[0].Start.Should().Be(0UL);
        ranges[0].End.Should().Be(63UL);
    }

    [Fact]
    public void Cover_ShouldCoalesceAdjacentRanges()
    {
        var encoder = new MortonIndexEncoder(2, precisionBits: 3);
        var bounds = BoundingBox.From2D(0, 0, 0.5, 0.25);

        var ranges = encoder.Cover(bounds, maxCells: 2);

        ranges.Should().NotBeEmpty();
        ranges.Should().OnlyContain(range => range.Start <= range.End);
        ranges.Count.Should().BeLessOrEqualTo(2);
    }

    private static ulong Encode(MortonIndexEncoder encoder, params double[] values)
    {
        return encoder.Encode(values);
    }
}
