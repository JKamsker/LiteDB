using System.Collections.Generic;

namespace LiteDB.Spatial.Testing.Oracles.Abstractions;

public interface IOracleMetadata
{
    string Name { get; }
    bool IsAvailable { get; }
    string? SkipReason { get; }
}

public interface IGeodesicOracle : IOracleMetadata
{
    double GetDistanceMeters(GeoCoordinate start, GeoCoordinate end);
}

public interface IGeometryOracle2D : IOracleMetadata
{
    double GetAreaSquareMeters(IEnumerable<IReadOnlyList<GeoPoint2D>> rings, bool areRingsClosed = true);
    double GetPerimeterMeters(IEnumerable<IReadOnlyList<GeoPoint2D>> rings, bool areRingsClosed = true);
}

public interface IDistanceOracle3D : IOracleMetadata
{
    double GetDistance(GeoPoint3D first, GeoPoint3D second);
}
