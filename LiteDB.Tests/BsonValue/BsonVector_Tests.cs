using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using LiteDB;
using LiteDB.Vector;
using Xunit;

namespace LiteDB.Tests.BsonValue_Types;

public class BsonVector_Tests
{
    [Fact]
    public void BsonVector_RoundTrip_Success()
    {
        var original = new BsonDocument
        {
            ["vec"] = new BsonVector(new float[] { 1.0f, 2.5f, -3.75f })
        };

        var bytes = BsonSerializer.Serialize(original);
        var deserialized = BsonSerializer.Deserialize(bytes);

        var vec = deserialized["vec"].AsVector;
        Assert.Equal(3, vec.Length);
        Assert.Equal(1.0f, vec[0]);
        Assert.Equal(2.5f, vec[1]);
        Assert.Equal(-3.75f, vec[2]);
    }

    [Fact]
    public void BsonVector_RoundTrip_UInt16Limit()
    {
        var values = Enumerable.Range(0, ushort.MaxValue).Select(i => (float)(i % 32)).ToArray();

        var original = new BsonDocument
        {
            ["vec"] = new BsonVector(values)
        };

        var bytes = BsonSerializer.Serialize(original);
        var deserialized = BsonSerializer.Deserialize(bytes);

        deserialized["vec"].AsVector.Should().Equal(values);
    }

    [Fact]
    public void BsonVector_CompareTo_SortsLexicographically()
    {
        var values = new List<BsonValue>
        {
            new BsonVector(new float[] { 1.0f }),
            new BsonVector(new float[] { 0.0f, 2.0f }),
            new BsonVector(new float[] { 0.0f, 1.0f, 0.5f }),
            new BsonVector(new float[] { 0.0f, 1.0f })
        };

        values.Sort();

        values.Should().Equal(
            new BsonVector(new float[] { 0.0f, 1.0f }),
            new BsonVector(new float[] { 0.0f, 1.0f, 0.5f }),
            new BsonVector(new float[] { 0.0f, 2.0f }),
            new BsonVector(new float[] { 1.0f }));
    }
}
