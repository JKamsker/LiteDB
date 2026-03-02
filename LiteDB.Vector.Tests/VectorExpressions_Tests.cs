using System;
using System.Linq;
using FluentAssertions;
using LiteDB;
using LiteDB.Vector;
using Xunit;

namespace LiteDB.Vector.Tests
{
    public class VectorExpressions_Tests
    {
        private static LiteDatabase CreateDatabase()
        {
            return new LiteDatabase(":memory:", plugins: new[] { VectorSearchPlugin.Instance });
        }

        private static BsonExpression CreateExpression(LiteDatabase db, string source, params BsonValue[] args)
        {
            return BsonExpression.Create(source, db.Services.ExpressionRegistry, args);
        }

        [Fact]
        public void VectorDist_DefaultCosine_SatisfiesThreshold()
        {
            using var db = CreateDatabase();
            var doc = new BsonDocument { ["Embedding"] = new BsonArray { 1.0, 0.0 } };

            var closePredicate = CreateExpression(db, "($.Embedding VECTOR_DIST [1.0, 0.0]) <= 0.1");
            closePredicate.ExecuteScalar(doc).AsBoolean.Should().BeTrue();

            var farPredicate = CreateExpression(db, "($.Embedding VECTOR_DIST [0.0, 1.0]) < 0.5");
            farPredicate.ExecuteScalar(doc).AsBoolean.Should().BeFalse();
        }

        [Fact]
        public void VectorDist_MetricOverride_UsesEuclideanFormula()
        {
            using var db = CreateDatabase();
            db.Services.ExpressionRegistry.Functions.Should().Contain(f =>
                string.Equals(f.Name, "VECTOR_DIST", StringComparison.OrdinalIgnoreCase) &&
                f.AdditionalArgumentCount == 1);

            var euclidean = CreateExpression(db, "VECTOR_DIST($.Embedding, [0.0, 1.0], @0)", new BsonValue("euclidean"));
            var cosine = CreateExpression(db, "VECTOR_DIST($.Embedding, [0.0, 1.0])");

            var doc = new BsonDocument { ["Embedding"] = new BsonArray { 1.0, 0.0 } };

            euclidean.ExecuteScalar(doc).AsDouble.Should().BeApproximately(Math.Sqrt(2), 1e-6);
            cosine.ExecuteScalar(doc).AsDouble.Should().BeApproximately(1.0, 1e-6);
        }

        [Fact]
        public void VectorExpressions_ProjectDistance_ReturnsExpectedValue()
        {
            using var db = CreateDatabase();
            var distanceProjection = CreateExpression(db, "VECTOR_DIST($.Embedding, [1.0, 0.0])");
            var doc = new BsonDocument { ["Embedding"] = new BsonArray { 1.0, 1.0 } };

            var distance = distanceProjection.ExecuteScalar(doc);
            distance.IsDouble.Should().BeTrue();
            distance.AsDouble.Should().BeApproximately(1 - (1 / Math.Sqrt(2)), 1e-6);
        }

        [Fact]
        public void VectorExpressions_ProjectSimilarity_ReturnsCosineValue()
        {
            using var db = CreateDatabase();
            var similarityProjection = CreateExpression(db, "VECTOR_SIM($.Embedding, [1.0, 0.0])");
            var doc = new BsonDocument { ["Embedding"] = new BsonArray { 1.0, 0.0 } };

            var similarity = similarityProjection.ExecuteScalar(doc);
            similarity.IsDouble.Should().BeTrue();
            similarity.AsDouble.Should().BeApproximately(1.0, 1e-6);
        }

        [Fact]
        public void VectorExpressions_ProjectSimilarity_SupportsDotProduct()
        {
            using var db = CreateDatabase();
            var similarityProjection = CreateExpression(db, "VECTOR_SIM($.Embedding, [1.0, 0.0], 'DotProduct')");
            var doc = new BsonDocument { ["Embedding"] = new BsonArray { 2.0, 3.0 } };

            var similarity = similarityProjection.ExecuteScalar(doc);
            similarity.IsDouble.Should().BeTrue();
            similarity.AsDouble.Should().BeApproximately(2.0, 1e-6);
        }

        [Fact]
        public void VectorExpressions_InvalidInput_YieldsNull()
        {
            using var db = CreateDatabase();
            var projection = CreateExpression(db, "VECTOR_DIST($.Embedding, [1.0, 0.0])");

            var invalidDoc = new BsonDocument { ["Embedding"] = new BsonArray { "a", "b" } };
            projection.ExecuteScalar(invalidDoc).IsNull.Should().BeTrue();

            var mismatchedDoc = new BsonDocument { ["Embedding"] = new BsonArray { 1.0, 0.0 } };
            CreateExpression(db, "VECTOR_DIST($.Embedding, [1.0, 0.0, 0.0])")
                .ExecuteScalar(mismatchedDoc)
                .IsNull.Should().BeTrue();
        }

        [Fact]
        public void VectorDist_ComposesWithArithmeticOperators()
        {
            using var db = CreateDatabase();
            var expression = CreateExpression(db, "1 + VECTOR_DIST($.Embedding, [0.0, 1.0])");
            var doc = new BsonDocument { ["Embedding"] = new BsonArray { 0.0, 1.0 } };

            expression.Type.Should().Be(BsonExpressionType.Add);
            expression.ExecuteScalar(doc).AsDouble.Should().BeApproximately(1.0, 1e-6);
        }

        [Fact]
        public void VectorDist_ComposesWithLogicalOperators()
        {
            using var db = CreateDatabase();
            var predicate = CreateExpression(db, "(($.Embedding VECTOR_DIST [1.0, 0.0]) <= 0.1) AND $.Flag");

            predicate.Type.Should().Be(BsonExpressionType.And);

            var matchingDoc = new BsonDocument
            {
                ["Embedding"] = new BsonArray { 1.0, 0.0 },
                ["Flag"] = true
            };

            predicate.ExecuteScalar(matchingDoc).AsBoolean.Should().BeTrue();

            var nonMatchingDoc = new BsonDocument
            {
                ["Embedding"] = new BsonArray { 0.0, 1.0 },
                ["Flag"] = true
            };

            predicate.ExecuteScalar(nonMatchingDoc).AsBoolean.Should().BeFalse();
        }

        [Fact]
        public void VectorSimilarity_ThrowsForUnsupportedMetric()
        {
            var left = new BsonArray { 1.0, 0.0 };
            var right = new BsonArray { 1.0, 0.0 };

            Action action = () => VectorExpressions.VectorSimilarity(left, right, VectorDistanceMetric.Euclidean);

            action.Should().Throw<LiteException>()
                .Where(ex => ex.Message.Contains("does not support similarity", StringComparison.OrdinalIgnoreCase));
        }
    }
}
