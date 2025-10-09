extern alias LiteDbBase;

#nullable enable

using System;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.GeoJson;

public sealed class GeoJsonSerializerTests
{
    [Fact]
    public void GeoPointRoundTrips()
    {
        var point = new GeoPoint(12.5, 48.1);
        var bson = GeoJsonSerializer.ToBson(point);

        bson.Should().NotBeNull();
        bson.AsDocument["type"].AsString.Should().Be("Point");

        var roundTripped = GeoJsonSerializer.FromBson<GeoPoint>(bson);
        roundTripped.Should().Be(point);
    }

    [Fact]
    public void LineStringRoundTrips()
    {
        var line = new GeoLineString(new[] { new GeoPoint(0, 0), new GeoPoint(1, 1), new GeoPoint(2, 1) });
        var bson = GeoJsonSerializer.ToBson(line);
        bson.AsDocument["type"].AsString.Should().Be("LineString");

        var roundTripped = GeoJsonSerializer.FromBson<GeoLineString>(bson);
        roundTripped.Points.Should().Equal(line.Points);
    }

    [Fact]
    public void PolygonRoundTrips()
    {
        var outer = new[]
        {
            new GeoPoint(0, 0),
            new GeoPoint(0, 2),
            new GeoPoint(2, 2),
            new GeoPoint(0, 0)
        };
        var polygon = new GeoPolygon(outer);

        var bson = GeoJsonSerializer.ToBson(polygon);
        bson.AsDocument["type"].AsString.Should().Be("Polygon");

        var roundTripped = GeoJsonSerializer.FromBson<GeoPolygon>(bson);
        roundTripped.Outer.Should().Equal(polygon.Outer);
    }

    [Fact]
    public void NullPayloadReturnsNull()
    {
        var result = GeoJsonSerializer.FromBson(BaseLiteDB.BsonValue.Null);
        result.Should().BeNull();
    }

    [Fact]
    public void InvalidTypeThrows()
    {
        var document = new BaseLiteDB.BsonDocument
        {
            ["type"] = "Unknown",
            ["coordinates"] = new BaseLiteDB.BsonArray()
        };

        BaseLiteDB.BsonValue value = document;
        Action action = () => GeoJsonSerializer.FromBson(value);
        action.Should().Throw<ArgumentException>().WithMessage("*Unknown*");
    }
}
