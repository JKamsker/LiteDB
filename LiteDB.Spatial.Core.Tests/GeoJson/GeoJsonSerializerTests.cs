extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.GeoJson;

public sealed class GeoJsonSerializerTests
{
    [Fact]
    public void PointRoundTrips()
    {
        var point = new GeoPoint(12.5, -42.3);

        var bson = GeoJsonSerializer.ToBson(point);
        bson.IsDocument.Should().BeTrue();

        var roundTrip = GeoJsonSerializer.FromBson<GeoPoint>(bson);
        roundTrip.Should().Be(point);

        GeoJsonSerializer.FromBson(bson).Should().Be(point);
    }

    [Fact]
    public void LineStringRoundTrips()
    {
        var line = new GeoLineString(new List<GeoPoint>
        {
            new GeoPoint(0, 0),
            new GeoPoint(1, 1),
            new GeoPoint(2, 0)
        });

        var bson = GeoJsonSerializer.ToBson(line);
        bson.AsDocument["type"].AsString.Should().Be("LineString");

        var roundTrip = GeoJsonSerializer.FromBson<GeoLineString>(bson);
        roundTrip.Points.Should().Equal(line.Points);
    }

    [Fact]
    public void PolygonRoundTrips()
    {
        var outer = new List<GeoPoint>
        {
            new GeoPoint(0, 0),
            new GeoPoint(2, 0),
            new GeoPoint(2, 2),
            new GeoPoint(0, 2),
            new GeoPoint(0, 0)
        };

        var hole = new List<GeoPoint>
        {
            new GeoPoint(0.5, 0.5),
            new GeoPoint(1.5, 0.5),
            new GeoPoint(1.5, 1.5),
            new GeoPoint(0.5, 0.5)
        };

        var polygon = new GeoPolygon(outer, new[] { hole });

        var bson = GeoJsonSerializer.ToBson(polygon);
        bson.AsDocument["type"].AsString.Should().Be("Polygon");

        var roundTrip = GeoJsonSerializer.FromBson<GeoPolygon>(bson);
        roundTrip.Outer.Should().Equal(polygon.Outer);
        roundTrip.Holes.Should().HaveCount(1);
        roundTrip.Holes[0].Should().Equal(polygon.Holes[0]);
    }

    [Fact]
    public void FromBsonThrowsOnUnsupportedGeometry()
    {
        var document = new BaseLiteDB.BsonDocument
        {
            ["type"] = "Unsupported",
            ["coordinates"] = new BaseLiteDB.BsonArray()
        };

        Action act = () => GeoJsonSerializer.FromBson(document);
        act.Should().Throw<FormatException>().WithMessage("*Unsupported GeoJSON geometry type*");
    }

    [Fact]
    public void NullPayloadHandledGracefully()
    {
        GeoJsonSerializer.ToBson(null).Should().Be(BaseLiteDB.BsonValue.Null);
        GeoJsonSerializer.FromBson(BaseLiteDB.BsonValue.Null).Should().BeNull();
    }
}
