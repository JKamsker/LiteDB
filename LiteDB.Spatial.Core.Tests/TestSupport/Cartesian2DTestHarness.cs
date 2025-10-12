extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BaseLiteDB = LiteDbBase::LiteDB;
using LiteDB.Spatial;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal sealed class Cartesian2DTestHarness : IDisposable
{
    private readonly BaseLiteDB.LiteDatabase _database;
    private readonly BaseLiteDB.ILiteCollection<CartesianPoint> _collection;
    private readonly BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> _rawCollection;

    public Cartesian2DTestHarness(BoundingBox domain)
    {
        _database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        _collection = _database.GetCollection<CartesianPoint>("cartesian_points");
        Descriptor = Spatial.UseCartesian2D(_collection, x => x.Position, domain);
        _rawCollection = _database.GetCollection("cartesian_points");
    }

    public SpatialCollectionDescriptor Descriptor { get; }

    public BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> RawCollection => _rawCollection;

    public void InsertPoints(IEnumerable<CartesianPoint> points)
    {
        _collection.DeleteAll();
        _collection.InsertBulk(points);
        Spatial.EnsurePointIndex(_collection);
    }

    public IReadOnlyList<int> QueryWithinBounds(BoundingBox bounds)
    {
        var results = Spatial.WithinBoundingBox(_collection, x => x.Position, bounds);
        return results.Select(point => point.Id).ToList();
    }

    public IReadOnlyList<CartesianPoint> QueryNear(GeoPoint center, double radius)
    {
        return Spatial.Near(_collection, x => x.Position, center, radius).ToList();
    }

    public ISpatialQueryPlan PlanNear(GeoPoint center, double radius)
    {
        return SpatialCartesian2D.Near(Descriptor, center, radius);
    }

    public IReadOnlyList<CartesianPoint> QueryByExpression(BaseLiteDB.BsonExpression? expression)
    {
        return expression == null ? _collection.FindAll().ToList() : _collection.Find(expression).ToList();
    }

    public IReadOnlyList<BaseLiteDB.BsonDocument> FetchRawDocuments()
    {
        return _rawCollection.FindAll().ToList();
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    internal sealed record CartesianPoint(int Id, GeoPoint Position);
}
