extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Linq.Expressions;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Linq;

public sealed class SpatialQueryableExtensionsTests
{
    [Fact]
    public void WhereNear_Expression_ComposesSpatialCall()
    {
        var source = new StubLiteQueryable<PointDocument>();
        var center = new GeoPoint(10.25, -47.5);

        var returned = source.WhereNear(x => x.Location, center, 125.5, GeographicDistanceMode.Vincenty);

        returned.Should().BeSameAs(source);
        source.LastPredicate.Should().NotBeNull();
        var predicate = source.LastPredicate!;
        var call = predicate.Body.Should().BeAssignableTo<MethodCallExpression>().Which;

        call.Method.DeclaringType.Should().Be(typeof(SpatialExpressions));
        call.Method.Name.Should().Be(nameof(SpatialExpressions.Near));

        call.Arguments[0].Should().BeAssignableTo<MemberExpression>().Which.Member.Name.Should().Be(nameof(PointDocument.Location));

        call.Arguments[1].Should().BeAssignableTo<ConstantExpression>().Which.Value.Should().Be(center);

        call.Arguments[2].Should().BeAssignableTo<ConstantExpression>().Which.Value.Should().Be(125.5);

        call.Arguments[3].Should().BeAssignableTo<ConstantExpression>().Which.Value.Should().Be(GeographicDistanceMode.Vincenty);
    }

    [Fact]
    public void WhereNear_NullableSelectorInsertsConvert()
    {
        var source = new StubLiteQueryable<NullablePointDocument>();
        var center = new GeoPoint(5.5, 6.5);

        source.WhereNear(x => x.Location, center, 50);

        source.LastPredicate.Should().NotBeNull();
        var predicate = source.LastPredicate!;
        var call = predicate.Body.Should().BeAssignableTo<MethodCallExpression>().Which;

        call.Arguments[0].Should().BeOfType<UnaryExpression>().Which.NodeType.Should().Be(ExpressionType.Convert);
    }

    [Theory]
    [InlineData("Location", "$.Location")]
    [InlineData("  location  ", "$.location")]
    [InlineData("Items[ * ].Position", "$.Items[ * ].Position")]
    public void BuildFieldReference_NormalizesFieldPaths(string input, string expected)
    {
        var method = typeof(SpatialQueryableExtensions).GetMethod("BuildFieldReference", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, new object[] { input })!;
        result.Should().Be(expected);
    }

    [Fact]
    public void BuildFieldReference_ThrowsForEmptyField()
    {
        var method = typeof(SpatialQueryableExtensions).GetMethod("BuildFieldReference", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        Action act = () => method!.Invoke(null, new object[] { "  " });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentException>()
            .Which.ParamName.Should().Be("geometryField");
    }

    [Fact]
    public void CreateGeoPointValue_ProducesDocument()
    {
        var method = typeof(SpatialQueryableExtensions).GetMethod("CreateGeoPointValue", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var value = (BaseLiteDB.BsonValue)method!.Invoke(null, new object[] { new GeoPoint(12.5, -8.75) })!;
        var document = value.AsDocument;

        document["Longitude"].AsDouble.Should().Be(12.5);
        document["Latitude"].AsDouble.Should().Be(-8.75);
    }

    [Fact]
    public void CreateGeoPoint3DValue_ProducesDocument()
    {
        var method = typeof(SpatialQueryableExtensions).GetMethod("CreateGeoPoint3DValue", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var value = (BaseLiteDB.BsonValue)method!.Invoke(null, new object[] { new GeoPoint3D(1, 2, 3) })!;
        var document = value.AsDocument;

        document["X"].AsDouble.Should().Be(1);
        document["Y"].AsDouble.Should().Be(2);
        document["Z"].AsDouble.Should().Be(3);
    }

    [Fact]
    public void CreateDistanceModeValue_ReturnsNullWhenModeMissing()
    {
        var method = typeof(SpatialQueryableExtensions).GetMethod("CreateDistanceModeValue", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var value = (BaseLiteDB.BsonValue)method!.Invoke(null, new object?[] { (GeographicDistanceMode?)null })!;
        value.IsNull.Should().BeTrue();

        var haversine = (BaseLiteDB.BsonValue)method.Invoke(null, new object?[] { GeographicDistanceMode.Haversine })!;
        haversine.AsString.Should().Be("Haversine");
    }

    [Fact]
    public void WhereNear_ThrowsForNegativeRadius()
    {
        var source = new StubLiteQueryable<PointDocument>();

        Action action = () => source.WhereNear("Location", new GeoPoint(0, 0), -1);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("radius");
    }

    private sealed class PointDocument
    {
        public GeoPoint Location { get; set; }
    }

    private sealed class NullablePointDocument
    {
        public GeoPoint? Location { get; set; }
    }

    private sealed class StubLiteQueryable<T> : BaseLiteDB.ILiteQueryable<T>
    {
        public Expression<Func<T, bool>>? LastPredicate { get; private set; }

        public BaseLiteDB.ILiteQueryable<T> Include(BaseLiteDB.BsonExpression path) => ThrowSelf();
        public BaseLiteDB.ILiteQueryable<T> Include(List<BaseLiteDB.BsonExpression> paths) => ThrowSelf();
        public BaseLiteDB.ILiteQueryable<T> Include<K>(Expression<Func<T, K>> path) => ThrowSelf();

        public BaseLiteDB.ILiteQueryable<T> Where(BaseLiteDB.BsonExpression predicate) => ThrowSelf();
        public BaseLiteDB.ILiteQueryable<T> Where(string predicate, BaseLiteDB.BsonDocument parameters) => ThrowSelf();
        public BaseLiteDB.ILiteQueryable<T> Where(string predicate, params BaseLiteDB.BsonValue[] args) => ThrowSelf();

        public BaseLiteDB.ILiteQueryable<T> Where(Expression<Func<T, bool>> predicate)
        {
            LastPredicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            return this;
        }

        public BaseLiteDB.ILiteQueryable<T> OrderBy(BaseLiteDB.BsonExpression keySelector, int order = 1) => ThrowSelf();
        public BaseLiteDB.ILiteQueryable<T> OrderBy<K>(Expression<Func<T, K>> keySelector, int order = 1) => ThrowSelf();
        public BaseLiteDB.ILiteQueryable<T> OrderByDescending(BaseLiteDB.BsonExpression keySelector) => ThrowSelf();
        public BaseLiteDB.ILiteQueryable<T> OrderByDescending<K>(Expression<Func<T, K>> keySelector) => ThrowSelf();
        public BaseLiteDB.ILiteQueryable<T> ThenBy(BaseLiteDB.BsonExpression keySelector) => ThrowSelf();
        public BaseLiteDB.ILiteQueryable<T> ThenBy<K>(Expression<Func<T, K>> keySelector) => ThrowSelf();
        public BaseLiteDB.ILiteQueryable<T> ThenByDescending(BaseLiteDB.BsonExpression keySelector) => ThrowSelf();
        public BaseLiteDB.ILiteQueryable<T> ThenByDescending<K>(Expression<Func<T, K>> keySelector) => ThrowSelf();
        public BaseLiteDB.ILiteQueryable<System.Linq.IGrouping<K, T>> GroupBy<K>(Expression<Func<T, K>> keySelector) => ThrowQueryable<System.Linq.IGrouping<K, T>>();
        public BaseLiteDB.ILiteQueryable<T> GroupBy(BaseLiteDB.BsonExpression keySelector) => ThrowSelf();
        public BaseLiteDB.ILiteQueryable<T> Having(BaseLiteDB.BsonExpression predicate) => ThrowSelf();
        public BaseLiteDB.ILiteQueryable<BaseLiteDB.BsonDocument> Select(BaseLiteDB.BsonExpression selector) => ThrowQueryable<BaseLiteDB.BsonDocument>();
        public BaseLiteDB.ILiteQueryable<K> Select<K>(Expression<Func<T, K>> selector) => ThrowQueryable<K>();

        public BaseLiteDB.ILiteQueryableResult<T> Limit(int limit) => ThrowResult();
        public BaseLiteDB.ILiteQueryableResult<T> Skip(int offset) => ThrowResult();
        public BaseLiteDB.ILiteQueryableResult<T> Offset(int offset) => ThrowResult();
        public BaseLiteDB.ILiteQueryableResult<T> ForUpdate() => ThrowResult();

        public BaseLiteDB.BsonDocument GetPlan() => ThrowValue<BaseLiteDB.BsonDocument>();
        public BaseLiteDB.IBsonDataReader ExecuteReader() => ThrowValue<BaseLiteDB.IBsonDataReader>();
        public IEnumerable<BaseLiteDB.BsonDocument> ToDocuments() => ThrowValue<IEnumerable<BaseLiteDB.BsonDocument>>();
        public IEnumerable<T> ToEnumerable() => ThrowValue<IEnumerable<T>>();
        public List<T> ToList() => ThrowValue<List<T>>();
        public T[] ToArray() => ThrowValue<T[]>();
        public int Into(string newCollection, BaseLiteDB.BsonAutoId autoId = BaseLiteDB.BsonAutoId.ObjectId) => ThrowValue<int>();
        public T First() => ThrowValue<T>();
        public T FirstOrDefault() => ThrowValue<T>();
        public T Single() => ThrowValue<T>();
        public T SingleOrDefault() => ThrowValue<T>();
        public int Count() => ThrowValue<int>();
        public long LongCount() => ThrowValue<long>();
        public bool Exists() => ThrowValue<bool>();

        private static BaseLiteDB.ILiteQueryable<T> ThrowSelf() => throw new NotSupportedException("This stub only records Where invocations.");
        private static BaseLiteDB.ILiteQueryable<TValue> ThrowQueryable<TValue>() => throw new NotSupportedException("This stub only records Where invocations.");
        private static BaseLiteDB.ILiteQueryableResult<T> ThrowResult() => throw new NotSupportedException("This stub only records Where invocations.");
        private static TReturn ThrowValue<TReturn>() => throw new NotSupportedException("This stub only records Where invocations.");
    }
}
