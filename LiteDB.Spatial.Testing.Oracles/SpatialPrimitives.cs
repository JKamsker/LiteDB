using System;
using System.Diagnostics.CodeAnalysis;

namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// Represents a longitude/latitude pair in degrees.
/// </summary>
public readonly record struct GeoCoordinate(double Longitude, double Latitude)
{
    public override string ToString() => $"({Longitude}, {Latitude})";
}

/// <summary>
/// Represents a 2D cartesian point.
/// </summary>
public readonly record struct Point2D(double X, double Y)
{
    public override string ToString() => $"({X}, {Y})";
}

/// <summary>
/// Represents a 3D cartesian point.
/// </summary>
public readonly record struct Point3D(double X, double Y, double Z)
{
    public override string ToString() => $"({X}, {Y}, {Z})";
}

/// <summary>
/// Lightweight handle that hides oracle-specific geometry implementations.
/// </summary>
public readonly struct GeometryHandle : IEquatable<GeometryHandle>
{
    private readonly object? _instance;

    internal GeometryHandle(object? instance)
    {
        _instance = instance;
    }

    internal object Instance => _instance ?? throw new InvalidOperationException("Geometry handle is empty.");

    public bool Equals(GeometryHandle other) => ReferenceEquals(_instance, other._instance);

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is GeometryHandle handle && Equals(handle);
    }

    public override int GetHashCode()
    {
        return _instance?.GetHashCode() ?? 0;
    }
}
