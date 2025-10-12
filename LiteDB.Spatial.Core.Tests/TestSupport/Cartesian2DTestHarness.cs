extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial;
using BaseLiteDB = LiteDbBase::LiteDB;
using BaseGeoPoint = LiteDbBase::LiteDB.Spatial.GeoPoint;
using BaseGeoPolygon = LiteDbBase::LiteDB.Spatial.GeoPolygon;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal sealed class Cartesian2DTestHarness : IDisposable
{
    private readonly BaseLiteDB.LiteDatabase _database;
    private readonly BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> _collection;
    private readonly SpatialMetadataStore _metadata;
    private SpatialCollectionDescriptor _descriptor;
    private readonly BoundingBox _domain;
    private readonly string _geometryFieldName = "position";
    private readonly List<PointRecord> _records = new();

    public Cartesian2DTestHarness(BoundingBox domain)
    {
        _database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        _collection = _database.GetCollection("points");
        _metadata = new SpatialMetadataStore(_database);
        _domain = domain;
        _descriptor = SpatialCartesian2D.EnsurePointIndex(_metadata, _collection, _geometryFieldName, domain);
    }

    public IReadOnlyList<PointRecord> Records => _records;

    public SpatialCollectionDescriptor Descriptor => _descriptor;

    public BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> RawCollection => _collection;

    public void InsertPoints(IEnumerable<CartesianPoint2D> points)
    {
        if (_records.Count > 0)
        {
            throw new InvalidOperationException("Points already inserted into harness.");
        }

        var list = points.Select((p, index) => new PointRecord(index + 1, p)).ToList();
        foreach (var record in list)
        {
            var document = new BaseLiteDB.BsonDocument
            {
                ["_id"] = record.Id,
                [_geometryFieldName] = new BaseLiteDB.BsonDocument
                {
                    ["x"] = record.Point.X,
                    ["y"] = record.Point.Y
                }
            };

            _collection.Insert(document);
        }

        _descriptor = SpatialCartesian2D.EnsurePointIndex(_metadata, _collection, _geometryFieldName, _domain, _descriptor.Options);
        _records.AddRange(list);
        _records.Should().NotBeEmpty("harness requires at least one point");
    }

    public IReadOnlyList<PointRecord> ExecuteNear((double X, double Y) center, double radius, out CandidateBreakdown breakdown)
    {
        var structCenter = new GeoPoint(center.X, center.Y);
        var plan = SpatialCartesian2D.Near(_descriptor, structCenter, radius);
        var candidates = EvaluateCandidates(plan, p =>
        {
            var distance = EuclideanDistance(p.Point, center);
            var tolerance = NumericTolerance.ForDistance(Math.Max(radius, distance));
            return distance <= radius + tolerance;
        });

        breakdown = candidates;
        return candidates.Exact.OrderBy(r => r.Id).ToList();
    }

    public IReadOnlyList<PointRecord> ExecuteBoundingBox(BoundingBox query, out CandidateBreakdown breakdown)
    {
        var plan = SpatialCartesian2D.WithinBoundingBox(_descriptor, query);
        var candidates = EvaluateCandidates(plan, p =>
        {
            return p.Point.X >= query.MinX && p.Point.X <= query.MaxX
                && p.Point.Y >= query.MinY && p.Point.Y <= query.MaxY;
        });
        breakdown = candidates;
        return candidates.Exact.OrderBy(r => r.Id).ToList();
    }

    public IReadOnlyList<PointRecord> ExecutePolygon(BaseGeoPolygon polygon, Func<CartesianPoint2D, bool> oracle, out CandidateBreakdown breakdown)
    {
        var bounds = ComputeBounds(polygon);
        var plan = SpatialCartesian2D.WithinBoundingBox(_descriptor, bounds);
        var candidates = EvaluateCandidates(plan, p => oracle(p.Point));
        breakdown = candidates;
        return candidates.Exact.OrderBy(r => r.Id).ToList();
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    private CandidateBreakdown EvaluateCandidates(ISpatialQueryPlan plan, Func<PointRecord, bool> exactPredicate)
    {
        var indexField = _descriptor.Options.IndexFieldName;
        var boundingField = _descriptor.Options.BoundingBoxFieldName;
        var raw = RawCollection.FindAll().ToList();
        var indexHits = new List<PointRecord>();
        var prefilterHits = new List<PointRecord>();
        var exactHits = new List<PointRecord>();

        foreach (var document in raw)
        {
            if (!document.TryGetValue(indexField, out var encoded))
            {
                continue;
            }

            var id = document["_id"].AsInt32;
            var record = _records.First(r => r.Id == id);
            if (!IsIndexMatch(encoded, plan.IndexRanges))
            {
                continue;
            }

            indexHits.Add(record);

            if (!document.TryGetValue(boundingField, out var bboxValue) || !bboxValue.IsArray)
            {
                continue;
            }

            var storedBounds = BoundingBox.Create(bboxValue.AsArray.Select(v => v.AsDouble).ToArray());
            if (plan.CoveringBounds is { } covering && !Intersects(storedBounds, covering))
            {
                continue;
            }

            prefilterHits.Add(record);

            if (exactPredicate(record))
            {
                exactHits.Add(record);
            }
        }

        return new CandidateBreakdown(indexHits, prefilterHits, exactHits);
    }

    private static BoundingBox ComputeBounds(BaseGeoPolygon polygon)
    {
        var minX = polygon.Outer.Min(p => p.Lon);
        var maxX = polygon.Outer.Max(p => p.Lon);
        var minY = polygon.Outer.Min(p => p.Lat);
        var maxY = polygon.Outer.Max(p => p.Lat);
        return BoundingBox.From2D(minX, minY, maxX, maxY);
    }

    private static bool IsIndexMatch(BaseLiteDB.BsonValue value, IReadOnlyList<SpatialIndexRange> ranges)
    {
        if (!value.IsNumber)
        {
            return false;
        }

        ulong encoded = value.Type switch
        {
            BaseLiteDB.BsonType.Int64 => (ulong)value.AsInt64,
            BaseLiteDB.BsonType.Int32 => (ulong)value.AsInt32,
            BaseLiteDB.BsonType.Decimal => (ulong)value.AsDecimal,
            BaseLiteDB.BsonType.Double => (ulong)value.AsDouble,
            _ => 0
        };
        foreach (var range in ranges)
        {
            if (encoded >= range.Start && encoded <= range.End)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Intersects(BoundingBox a, BoundingBox b)
    {
        return a.MinX <= b.MaxX && a.MaxX >= b.MinX
            && a.MinY <= b.MaxY && a.MaxY >= b.MinY;
    }

    private static double EuclideanDistance(CartesianPoint2D left, (double X, double Y) right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public sealed record PointRecord(int Id, CartesianPoint2D Point);

    public sealed record CandidateBreakdown(IReadOnlyList<PointRecord> Index, IReadOnlyList<PointRecord> Prefilter, IReadOnlyList<PointRecord> Exact);
}
