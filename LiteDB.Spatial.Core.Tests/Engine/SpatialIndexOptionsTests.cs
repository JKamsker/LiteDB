using System;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine;

public class SpatialIndexOptionsTests
{
    [Fact]
    public void SpatialIndexOptions_ShouldUseSensibleDefaults()
    {
        var options = new SpatialIndexOptions();

        options.PrecisionBits.Should().Be(32);
        options.MaxCoveringCells.Should().Be(64);
        options.DistanceTolerance.Should().Be(0.001);
        options.IndexFieldName.Should().Be(SpatialIndexOptions.DefaultIndexFieldName);
        options.BoundingBoxFieldName.Should().Be(SpatialIndexOptions.DefaultBoundingBoxFieldName);
    }

    [Fact]
    public void SpatialIndexOptions_ShouldAllowCustomization()
    {
        var options = new SpatialIndexOptions(40, 128, 0.25, "_custom_idx", "_custom_mbb");

        options.PrecisionBits.Should().Be(40);
        options.MaxCoveringCells.Should().Be(128);
        options.DistanceTolerance.Should().Be(0.25);
        options.IndexFieldName.Should().Be("_custom_idx");
        options.BoundingBoxFieldName.Should().Be("_custom_mbb");
    }

    [Fact]
    public void SpatialIndexOptions_With_ShouldCreateNewInstance()
    {
        var options = new SpatialIndexOptions();
        var updated = options.With(distanceTolerance: 0.01);

        updated.Should().NotBeSameAs(options);
        updated.DistanceTolerance.Should().Be(0.01);
        updated.IndexFieldName.Should().Be(options.IndexFieldName);
    }

    [Theory]
    [InlineData(0, 1, 0.1, "_idx", "_mbb")]
    [InlineData(32, 0, 0.1, "_idx", "_mbb")]
    [InlineData(32, 1, -0.1, "_idx", "_mbb")]
    [InlineData(32, 1, 0.1, "", "_mbb")]
    [InlineData(32, 1, 0.1, "_idx", " ")]
    public void SpatialIndexOptions_ShouldValidateInputs(int precisionBits, int maxCells, double tolerance, string indexField, string mbbField)
    {
        Action act = () => new SpatialIndexOptions(precisionBits, maxCells, tolerance, indexField, mbbField);

        act.Should().Throw<ArgumentException>();
    }
}
