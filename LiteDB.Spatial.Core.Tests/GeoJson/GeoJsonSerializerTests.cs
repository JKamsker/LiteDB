extern alias LiteDbBase;

#nullable enable

using System;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.GeoJson;

public sealed class GeoJsonSerializerTests
{
    [Fact]
    public void PointRoundTrips()
    {
        var point = new GeoPoint(16.3738, 48.2082);
        var bson = GeoJsonSerializer.ToBson(point);
        var back = GeoJsonSerializer.Deserialize<GeoPoint>(bson);

        back.Should().Be(point);
    }

    [Fact]
    public void LineStringRoundTrips()
    {
        var line = new GeoLineString(new[]
        {
            new GeoPoint(0, 0),
            new GeoPoint(1, 1),
            new GeoPoint(2, 2)
        });

        var bson = GeoJsonSerializer.ToBson(line);
        var back = GeoJsonSerializer.Deserialize<GeoLineString>(bson);

        back.Points.Should().HaveCount(line.Points.Count);
        back.Points.Zip(line.Points).All(pair => pair.First.Equals(pair.Second)).Should().BeTrue();
    }

    [Fact]
    public void PolygonRoundTrips()
    {
        var outer = new[]
        {
            new GeoPoint(0, 0),
            new GeoPoint(0, 1),
            new GeoPoint(1, 1),
            new GeoPoint(1, 0),
            new GeoPoint(0, 0)
        };

        var hole = new[]
        {
            new GeoPoint(0.2, 0.2),
            new GeoPoint(0.2, 0.4),
            new GeoPoint(0.4, 0.4),
            new GeoPoint(0.2, 0.2)
        };

        var polygon = new GeoPolygon(outer, new[] { hole });
        var bson = GeoJsonSerializer.ToBson(polygon);
        var back = GeoJsonSerializer.Deserialize<GeoPolygon>(bson);

        back.Outer.Should().HaveCount(outer.Length);
        back.Holes.Should().HaveCount(1);
        back.Holes[0].Should().HaveCount(hole.Length);
    }

    [Fact]
    public void UnknownTypeThrows()
    {
        var document = new BaseLiteDB.BsonDocument
        {
            ["type"] = "Triangle",
            ["coordinates"] = new BaseLiteDB.BsonArray()
        };

        Action action = () => GeoJsonSerializer.Deserialize(document);
        action.Should().Throw<BaseLiteDB.LiteException>().WithMessage("*Unsupported GeoJSON geometry type*");
    }

    [Fact]
    public void NullValueReturnsNull()
    {
        GeoJsonSerializer.Deserialize(BaseLiteDB.BsonValue.Null).Should().BeNull();
    }
}
