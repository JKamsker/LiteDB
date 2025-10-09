extern alias LiteDbSpatial;
extern alias LiteDbSpatialCore;

using LiteDB;
using BoundingBox = LiteDbSpatialCore::LiteDB.Spatial.BoundingBox;
using GeoPoint = LiteDbSpatialCore::LiteDB.Spatial.GeoPoint;
using SpatialFacade = LiteDbSpatial::LiteDB.Spatial.Spatial;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

const string DatabasePath = "spatial-sample.db";

app.MapPost("/seed", () =>
{
    using var db = new LiteDatabase(DatabasePath);
    var places = db.GetCollection<Place>("places");
    SpatialFacade.UseGeographic(places);
    SpatialFacade.EnsurePointIndex(places, x => x.Location);

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
    SpatialFacade.UseGeographic(places);
    SpatialFacade.EnsurePointIndex(places, x => x.Location);

    var center = new GeoPoint(lon, lat);
    var radiusMeters = radiusKm * 1000;

    var results = SpatialFacade.Near(places, x => x.Location, center, radiusMeters)
        .Select(x => new { x.Name, x.Location.Latitude, x.Location.Longitude })
        .ToList();

    return Results.Ok(results);
});

app.MapGet("/places/within", () =>
{
    using var db = new LiteDatabase(DatabasePath);
    var places = db.GetCollection<Place>("places");
    SpatialFacade.UseGeographic(places);
    SpatialFacade.EnsurePointIndex(places, x => x.Location);

    var bounds = BoundingBox.From2D(16.0, 48.0, 17.0, 48.4);

    var results = SpatialFacade.WithinBoundingBox(places, x => x.Location, bounds)
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
