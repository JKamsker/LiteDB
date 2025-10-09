extern alias LiteDbSpatial;
extern alias LiteDbSpatialCore;

using LiteDB;
using BoundingBox = LiteDbSpatialCore::LiteDB.Spatial.BoundingBox;
using GeoPoint3D = LiteDbSpatialCore::LiteDB.Spatial.GeoPoint3D;
using SpatialFacade = LiteDbSpatial::LiteDB.Spatial.Spatial;

const string DatabasePath = "cartesian3d.db";

using var db = new LiteDatabase(DatabasePath);
var sensors = db.GetCollection<Sensor>("sensors");

var domain = BoundingBox.From3D(-100, -100, -100, 100, 100, 100);
SpatialFacade.UseCartesian3D(sensors, domain);
SpatialFacade.EnsurePointIndex(sensors, s => s.Position);

if (sensors.Count() == 0)
{
    sensors.Insert(new Sensor { Name = "Origin", Position = new GeoPoint3D(0, 0, 0) });
    sensors.Insert(new Sensor { Name = "Neighbor", Position = new GeoPoint3D(1.2, -0.4, 0.8) });
    sensors.Insert(new Sensor { Name = "Distant", Position = new GeoPoint3D(12, 8, -4) });
}

var center = new GeoPoint3D(0, 0, 0);
var closeSensors = SpatialFacade.Near(sensors, s => s.Position, center, radius: 3).ToList();

Console.WriteLine("Sensors within 3 units of the origin:");
foreach (var sensor in closeSensors)
{
    Console.WriteLine($" - {sensor.Name} at {sensor.Position}");
}

var bounds = BoundingBox.From3D(-2, -2, -2, 2, 2, 2);
var boxed = SpatialFacade.WithinBoundingBox(sensors, s => s.Position, bounds).ToList();

Console.WriteLine();
Console.WriteLine("Sensors inside the [-2,2] cube:");
foreach (var sensor in boxed)
{
    Console.WriteLine($" - {sensor.Name} at {sensor.Position}");
}

public class Sensor
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public GeoPoint3D Position { get; set; } = new GeoPoint3D(0, 0, 0);
}
