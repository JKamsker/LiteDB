using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Geometry;

public class GeoPoint3DTests
{
    [Fact]
    public void GeoPoint3D_ShouldBeReadOnlyStruct()
    {
        var type = typeof(GeoPoint3D);
        type.IsValueType.Should().BeTrue();
        type.IsDefined(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute), inherit: false).Should().BeTrue();

        var nonReadonlyFields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(field => !field.IsInitOnly)
            .ToList();

        nonReadonlyFields.Should().BeEmpty();
    }

    [Fact]
    public void GeoPoint3D_ShouldExposeCoordinates()
    {
        var point = new GeoPoint3D(1, 2, 3);

        point.X.Should().Be(1);
        point.Y.Should().Be(2);
        point.Z.Should().Be(3);
        point.ToString().Should().Be("(1, 2, 3)");
    }

    [Theory]
    [InlineData(double.NaN, 0, 0)]
    [InlineData(0, double.NaN, 0)]
    [InlineData(0, 0, double.NaN)]
    [InlineData(double.PositiveInfinity, 0, 0)]
    [InlineData(0, double.NegativeInfinity, 0)]
    [InlineData(0, 0, double.PositiveInfinity)]
    public void GeoPoint3D_ShouldRejectNonFiniteValues(double x, double y, double z)
    {
        Action act = () => new GeoPoint3D(x, y, z);

        act.Should().Throw<ArgumentException>();
    }
}
