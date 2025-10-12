using LiteDB;
using LiteDB.Spatial;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

const string DatabasePath = "spatial-sample.db";

app.MapPost("/seed", () =>
{
    using var db = new LiteDatabase(DatabasePath);
    var places = db.GetCollection<Place>("places");
    Spatial.UseGeographic(places, x => x.Location);

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
    using var db = new LiteDatabase(DatabasePath);
    var places = db.GetCollection<Place>("places");
    Spatial.EnsurePointIndex(places);

    var center = new GeoPoint(lon, lat);
    var radiusMeters = radiusKm * 1000;

    var results = Spatial.Near(places, x => x.Location, center, radiusMeters)
        .Select(x => new { x.Name, x.Location.Latitude, x.Location.Longitude })
        .ToList();

    return Results.Ok(results);
});

app.MapGet("/places/within", (double minLon, double minLat, double maxLon, double maxLat) =>
{
    using var db = new LiteDatabase(DatabasePath);
    var places = db.GetCollection<Place>("places");
    Spatial.EnsurePointIndex(places);

    var bounds = BoundingBox.From2D(minLon, minLat, maxLon, maxLat);

    var results = Spatial.WithinBoundingBox(places, x => x.Location, bounds)
        .Select(x => new { x.Name, x.Location.Latitude, x.Location.Longitude })
        .ToList();

    return Results.Ok(results);
});

app.Run();

public class Place
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public GeoPoint Location { get; set; } = new GeoPoint(0, 0);
}
