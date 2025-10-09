using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Geometry;

public class BoundingBoxTests
{
    [Fact]
    public void BoundingBox_ShouldBeReadOnlyStruct()
    {
        var type = typeof(BoundingBox);
        type.IsValueType.Should().BeTrue();
        type.IsDefined(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute), inherit: false).Should().BeTrue();

        var nonReadonlyFields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(field => !field.IsInitOnly)
            .ToList();

        nonReadonlyFields.Should().BeEmpty();
    }

    [Fact]
    public void BoundingBox2D_ShouldExposeExtentsAndDimensions()
    {
        var box = BoundingBox.From2D(-10, -5, 20, 15);

        box.Dimensions.Should().Be(2);
        box.Is3D.Should().BeFalse();
        box.MinX.Should().Be(-10);
        box.MinY.Should().Be(-5);
        box.MaxX.Should().Be(20);
        box.MaxY.Should().Be(15);
        box.GetValues().ToArray().Should().Equal(-10, -5, 20, 15);
    }

    [Fact]
    public void BoundingBox3D_ShouldExposeExtentsAndDimensions()
    {
        var box = BoundingBox.From3D(-1, -2, -3, 4, 5, 6);

        box.Dimensions.Should().Be(3);
        box.Is3D.Should().BeTrue();
        box.MinX.Should().Be(-1);
        box.MinY.Should().Be(-2);
        box.MinZ.Should().Be(-3);
        box.MaxX.Should().Be(4);
        box.MaxY.Should().Be(5);
        box.MaxZ.Should().Be(6);
        box.GetValues().ToArray().Should().Equal(-1, -2, -3, 4, 5, 6);
    }

    [Fact]
    public void BoundingBox_ShouldRejectInvalidAxisOrdering()
    {
        Action twoD = () => BoundingBox.From2D(5, 0, 4, 1);
        Action threeD = () => BoundingBox.From3D(0, 0, 0, -1, 2, 3);

        twoD.Should().Throw<ArgumentException>();
        threeD.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BoundingBox_ShouldRejectInvalidValueCount()
    {
        Action act = () => BoundingBox.Create(new[] { 1.0, 2.0, 3.0 });

        act.Should().Throw<ArgumentException>();
    }
}
