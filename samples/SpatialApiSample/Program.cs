using LiteDB;
using LiteDB.Plugins;
using LiteDB.Spatial;
using LiteDB.Spatial.Plugin;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

const string DatabasePath = "spatial-sample.db";

app.MapPost("/seed", () =>
{
    using var db = CreateDatabase();
    var places = db.GetCollection<Place>("places");
    places.EnsureIndex(x => x.Location);

    if (places.Count() > 0)
    {
        return Results.Ok(new { message = "Database already seeded." });
    }

    var vienna = new Place
    {
        Name = "Vienna",
        Location = new GeoPoint(16.3738, 48.2082)
    };

    var bratislava = new Place
    {
        Name = "Bratislava",
        Location = new GeoPoint(17.1077, 48.1486)
    };

    places.Insert(new[] { vienna, bratislava });

    return Results.Ok(new { message = "Seeded" });
});

app.MapGet("/places/near", (double lat, double lon, double radiusKm) =>
{
    using var db = CreateDatabase();
    var places = db.GetCollection<Place>("places");
    places.EnsureIndex(x => x.Location);

    var center = new GeoPoint(lon, lat);
    var radiusMeters = radiusKm * 1000;

    var results = places
        .Query()
        .WhereNear(x => x.Location, center, radiusMeters)
        .Select(x => new { x.Name, x.Location.Latitude, x.Location.Longitude })
        .ToList();

    return Results.Ok(results);
});

app.MapGet("/places/within", (double minLon, double minLat, double maxLon, double maxLat) =>
{
    using var db = CreateDatabase();
    var places = db.GetCollection<Place>("places");
    places.EnsureIndex(x => x.Location);

    var bounds = BoundingBox.From2D(minLon, minLat, maxLon, maxLat);

    var results = places
        .Query()
        .WhereWithinBox(x => x.Location, bounds)
        .Select(x => new { x.Name, x.Location.Latitude, x.Location.Longitude })
        .ToList();

    return Results.Ok(results);
});

app.Run();

LiteDatabase CreateDatabase()
{
    return new LiteDatabase(DatabasePath, plugins: new ILitePlugin[]
    {
        new SpatialPlugin()
    });
}

public class Place
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public GeoPoint Location { get; set; } = new GeoPoint(0, 0);
}
