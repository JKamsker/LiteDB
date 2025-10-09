namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// Provides Euclidean distance calculations in 3D using a reference implementation.
/// </summary>
public interface IDistanceOracle3D
{
    string Name { get; }

    bool IsEnabled { get; }

    double Distance(Point3D first, Point3D second);
}
