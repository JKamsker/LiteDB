using LiteDB;
using LiteDB.Vector;
using FluentAssertions;
using LiteDB.Vector.Utils;
using Xunit;

namespace LiteDB.Vector.Tests
{
    public class VectorMetricParsingEdgeCases_Tests
    {
        [Theory]
        [InlineData(".1")]
        [InlineData(".01")]
        [InlineData(".-0.1")]
        public void VectorMetricParser_TryParseString_Should_reject_leading_dot_numeric_tokens(string token)
        {
            VectorMetricParser.TryParseString(token, out _).Should().BeFalse();
        }

        [Theory]
        [InlineData(".1")]
        [InlineData(".01")]
        [InlineData(".-0.1")]
        public void VectorDist_InvalidMetricStringLiteral_Should_return_null_in_SQL_select(string token)
        {
            using var db = new LiteDatabase(":memory:", plugins: new[] { VectorSearchPlugin.Instance });

            var result = db.Execute($"SELECT VECTOR_DIST([1.0, 0.0], [1.0, 0.0], '{token}')").Single();
            result.IsDocument.Should().BeTrue();
            result.AsDocument["expr"].IsNull.Should().BeTrue();
        }

        [Theory]
        [InlineData(".1")]
        [InlineData(".01")]
        [InlineData(".-0.1")]
        public void VectorSim_InvalidMetricStringLiteral_Should_return_null_in_SQL_select(string token)
        {
            using var db = new LiteDatabase(":memory:", plugins: new[] { VectorSearchPlugin.Instance });

            var result = db.Execute($"SELECT VECTOR_SIM([1.0, 0.0], [1.0, 0.0], '{token}')").Single();
            result.IsDocument.Should().BeTrue();
            result.AsDocument["expr"].IsNull.Should().BeTrue();
        }

        [Theory]
        [InlineData(".1")]
        [InlineData(".01")]
        [InlineData(".-0.1")]
        public void VectorSqlFunctions_InvalidMetric_Should_return_null(string token)
        {
            using var db = new LiteDatabase(":memory:", plugins: new[] { VectorSearchPlugin.Instance });

            db.Services.SqlFunctions.TryGet("VECTOR_DIST", out var dist).Should().BeTrue();
            db.Services.SqlFunctions.TryGet("VECTOR_SIM", out var sim).Should().BeTrue();

            var left = new BsonArray { 1.0, 0.0 };
            var right = new BsonArray { 1.0, 0.0 };
            var metric = new BsonValue(token);

            dist.Implementation(new BsonValue[] { left, right, metric }).IsNull.Should().BeTrue();
            sim.Implementation(new BsonValue[] { left, right, metric }).IsNull.Should().BeTrue();
        }
    }
}
