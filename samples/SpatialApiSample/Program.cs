extern alias LiteDbBase;

using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial;
using BaseLiteDB = LiteDbBase::LiteDB;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

const string DatabasePath = "spatial-sample.db";

app.MapPost("/seed", () =>
{
    using var db = new BaseLiteDB.LiteDatabase(DatabasePath);
    var metadata = new SpatialMetadataStore(db);
    var places = db.GetCollection("places");
    var sensors = db.GetCollection("sensors");

    Spatial.UseGeographic(metadata, places.Name, "location");
    Spatial.UseCartesian3D(metadata, sensors.Name, "position", BoundingBox.From3D(-100, -100, -50, 100, 100, 50));

    Spatial.EnsurePointIndex(metadata, places);
    Spatial.EnsurePointIndex(metadata, sensors);

    if (places.Count() > 0 && sensors.Count() > 0)
    {
        return Results.Ok(new { message = "Database already seeded." });
    }

    places.Insert(new[]
    {
        CreatePlaceDocument(1, "Vienna", 16.3738, 48.2082),
        CreatePlaceDocument(2, "Bratislava", 17.1077, 48.1486),
        CreatePlaceDocument(3, "Prague", 14.4378, 50.0755)
    });

    sensors.Insert(new[]
    {
        CreateSensorDocument(1, 0, 0, 0),
        CreateSensorDocument(2, 5, -2, 1.5),
        CreateSensorDocument(3, -4, 3, -3)
    });

    return Results.Ok(new { message = "Seeded" });
});

app.MapGet("/places/near", (double lat, double lon, double radiusKm) =>
{
    using var db = new BaseLiteDB.LiteDatabase(DatabasePath);
    var metadata = new SpatialMetadataStore(db);
    var places = db.GetCollection("places");
    var descriptor = Spatial.EnsurePointIndex(metadata, places);
    var center = new GeoPoint(lon, lat);
    var plan = Spatial.Near(descriptor, center, radiusKm * 1000);

    var hits = EnumeratePlan2D(descriptor, places, plan)
        .Where(item => descriptor.Engine!.Distance.Distance(center, item.Point) <= radiusKm * 1000 + descriptor.Options.DistanceTolerance)
        .Select(item => new
        {
            id = item.Document["_id"].AsInt32,
            name = item.Document["name"].AsString,
            longitude = item.Point.Longitude,
            latitude = item.Point.Latitude
        })
        .ToList();

    return Results.Ok(hits);
});

app.MapGet("/places/within", () =>
{
    using var db = new BaseLiteDB.LiteDatabase(DatabasePath);
    var metadata = new SpatialMetadataStore(db);
    var places = db.GetCollection("places");
    var descriptor = Spatial.EnsurePointIndex(metadata, places);
    var bounds = BoundingBox.From2D(16.2, 48.0, 17.3, 48.4);
    var plan = Spatial.WithinBoundingBox(descriptor, bounds);

    var hits = EnumeratePlan2D(descriptor, places, plan)
        .Where(item => Contains(bounds, item.Point))
        .Select(item => new { id = item.Document["_id"].AsInt32, name = item.Document["name"].AsString })
        .ToList();

    return Results.Ok(hits);
});

app.MapGet("/sensors/near", (double x, double y, double z, double radius) =>
{
    using var db = new BaseLiteDB.LiteDatabase(DatabasePath);
    var metadata = new SpatialMetadataStore(db);
    var sensors = db.GetCollection("sensors");
    var descriptor = Spatial.EnsurePointIndex(metadata, sensors);
    var center = new GeoPoint3D(x, y, z);
    var plan = Spatial.Near(descriptor, center, radius);

    var hits = EnumeratePlan3D(descriptor, sensors, plan)
        .Where(item => descriptor.Engine!.Distance.Distance(center, item.Point) <= radius + descriptor.Options.DistanceTolerance)
        .Select(item => new
        {
            id = item.Document["_id"].AsInt32,
            x = item.Point.X,
            y = item.Point.Y,
            z = item.Point.Z
        })
        .ToList();

    return Results.Ok(hits);
});

app.Run();

static BaseLiteDB.BsonDocument CreatePlaceDocument(int id, string name, double longitude, double latitude)
{
    return new BaseLiteDB.BsonDocument
    {
        ["_id"] = id,
        ["name"] = name,
        ["location"] = new BaseLiteDB.BsonDocument
        {
            ["longitude"] = longitude,
            ["latitude"] = latitude
        }
    };
}

static BaseLiteDB.BsonDocument CreateSensorDocument(int id, double x, double y, double z)
{
    return new BaseLiteDB.BsonDocument
    {
        ["_id"] = id,
        ["position"] = new BaseLiteDB.BsonDocument
        {
            ["x"] = x,
            ["y"] = y,
            ["z"] = z
        }
    };
}

static IEnumerable<(BaseLiteDB.BsonDocument Document, GeoPoint Point)> EnumeratePlan2D(
    SpatialCollectionDescriptor descriptor,
    BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection,
    ISpatialQueryPlan plan)
{
    var engine = descriptor.Engine ?? throw new InvalidOperationException("Engine is not attached to descriptor.");
    var visited = new HashSet<int>();

    foreach (var range in plan.IndexRanges)
    {
        var query = collection.Query().Where(BaseLiteDB.Query.Between(descriptor.Options.IndexFieldName, ToIndexValue(range.Start), ToIndexValue(range.End)));
        foreach (var document in query.ToDocuments())
        {
            var id = document["_id"].AsInt32;
            if (!visited.Add(id))
            {
                continue;
            }

            if (!engine.Mapper.TryReadPoint(document, out GeoPoint point))
            {
                continue;
            }

            if (plan.CoveringBounds.HasValue && !Contains(plan.CoveringBounds.Value, point))
            {
                continue;
            }

            yield return (document, point);
        }
    }
}

static IEnumerable<(BaseLiteDB.BsonDocument Document, GeoPoint3D Point)> EnumeratePlan3D(
    SpatialCollectionDescriptor descriptor,
    BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection,
    ISpatialQueryPlan plan)
{
    var engine = descriptor.Engine ?? throw new InvalidOperationException("Engine is not attached to descriptor.");
    var visited = new HashSet<int>();

    foreach (var range in plan.IndexRanges)
    {
        var query = collection.Query().Where(BaseLiteDB.Query.Between(descriptor.Options.IndexFieldName, ToIndexValue(range.Start), ToIndexValue(range.End)));
        foreach (var document in query.ToDocuments())
        {
            var id = document["_id"].AsInt32;
            if (!visited.Add(id))
            {
                continue;
            }

            if (!engine.Mapper.TryReadPoint(document, out GeoPoint3D point))
            {
                continue;
            }

            if (plan.CoveringBounds.HasValue && !Contains(plan.CoveringBounds.Value, point))
            {
                continue;
            }

            yield return (document, point);
        }
    }
}

static BaseLiteDB.BsonValue ToIndexValue(ulong value)
{
    return value <= long.MaxValue
        ? new BaseLiteDB.BsonValue((long)value)
        : new BaseLiteDB.BsonValue((decimal)value);
}

static bool Contains(BoundingBox box, GeoPoint point)
{
    var values = box.GetValues();
    return point.Longitude >= values[0]
        && point.Latitude >= values[1]
        && point.Longitude <= values[2]
        && point.Latitude <= values[3];
}

static bool Contains(BoundingBox box, GeoPoint3D point)
{
    var values = box.GetValues();
    return point.X >= values[0]
        && point.Y >= values[1]
        && point.Z >= values[2]
        && point.X <= values[3]
        && point.Y <= values[4]
        && point.Z <= values[5];
}
