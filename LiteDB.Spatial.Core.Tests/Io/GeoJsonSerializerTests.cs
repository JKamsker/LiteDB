extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using BaseLiteDB = LiteDbBase::LiteDB;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Io;

public sealed class GeoJsonSerializerTests
{
    [Fact]
    public void RoundTripPoint()
    {
        var point = new GeoPoint(12.5, -45.25);
        var bson = GeoJsonSerializer.Serialize(point);

        bson.IsDocument.Should().BeTrue();
        bson.AsDocument["type"].AsString.Should().Be("Point");

        var deserialized = GeoJsonSerializer.DeserializePoint(bson);
        deserialized.Should().Be(point);

        GeoJsonSerializer.Deserialize(bson).Should().Be(point);
    }

    [Fact]
    public void RoundTripLineString()
    {
        var line = new GeoLineString(new List<GeoPoint>
        {
            new GeoPoint(0, 0),
            new GeoPoint(1, 1),
            new GeoPoint(2, 2)
        });

        var bson = GeoJsonSerializer.Serialize(line);
        bson.AsDocument["type"].AsString.Should().Be("LineString");

        var deserialized = GeoJsonSerializer.DeserializeLineString(bson);
        deserialized.Should().BeEquivalentTo(line, options => options.WithStrictOrdering());
        GeoJsonSerializer.Deserialize(bson).Should().BeEquivalentTo(line, options => options.WithStrictOrdering());
    }

    [Fact]
    public void RoundTripPolygon()
    {
        var outer = new List<GeoPoint>
        {
            new GeoPoint(0, 0),
            new GeoPoint(0, 1),
            new GeoPoint(1, 1),
            new GeoPoint(1, 0),
            new GeoPoint(0, 0)
        };
        var holes = new List<IReadOnlyList<GeoPoint>>
        {
            new List<GeoPoint>
            {
                new GeoPoint(0.2, 0.2),
                new GeoPoint(0.2, 0.4),
                new GeoPoint(0.4, 0.4),
                new GeoPoint(0.4, 0.2),
                new GeoPoint(0.2, 0.2)
            }
        };

        var polygon = new GeoPolygon(outer, holes);
        var bson = GeoJsonSerializer.Serialize(polygon);

        bson.AsDocument["type"].AsString.Should().Be("Polygon");

        var deserialized = GeoJsonSerializer.DeserializePolygon(bson);
        deserialized.Should().BeEquivalentTo(polygon, options => options.WithStrictOrdering());
        GeoJsonSerializer.Deserialize(bson).Should().BeEquivalentTo(polygon, options => options.WithStrictOrdering());
    }

    [Fact]
    public void DeserializeRequiresType()
    {
        var invalid = new BaseLiteDB.BsonDocument
        {
            ["coordinates"] = new BaseLiteDB.BsonArray { 0, 0 }
        };

        FluentActions.Invoking(() => GeoJsonSerializer.Deserialize(invalid)).Should().Throw<FormatException>();
    }
}

