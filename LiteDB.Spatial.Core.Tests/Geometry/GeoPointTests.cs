using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Geometry;

public class GeoPointTests
{
    [Fact]
    public void GeoPoint_ShouldBeReadOnlyStruct()
    {
        var type = typeof(GeoPoint);
        type.IsValueType.Should().BeTrue();
        type.IsDefined(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute), inherit: false).Should().BeTrue();

        var nonReadonlyFields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(field => !field.IsInitOnly)
            .ToList();

        nonReadonlyFields.Should().BeEmpty();
    }

    [Fact]
    public void GeoPoint_ShouldExposeCoordinates()
    {
        var point = new GeoPoint(12.5, -23.4);

        point.Longitude.Should().Be(12.5);
        point.Latitude.Should().Be(-23.4);
        point.ToString().Should().Be("(12.5, -23.4)");
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.NegativeInfinity)]
    public void GeoPoint_ShouldRejectNonFiniteValues(double longitude, double latitude)
    {
        Action act = () => new GeoPoint(longitude, latitude);

        act.Should().Throw<ArgumentException>();
    }
}
