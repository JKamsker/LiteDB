#nullable enable

using System;

namespace LiteDB.Spatial;

public sealed class CartesianNormalizer
{
    private readonly BoundingBox _domain;
    private readonly double _widthX;
    private readonly double _widthY;
    private readonly double _widthZ;

    public CartesianNormalizer(BoundingBox domain)
    {
        if (domain.Dimensions is < 2 or > 3)
        {
            throw new ArgumentException("Cartesian domains must be two- or three-dimensional.", nameof(domain));
        }

        _domain = domain;
        _widthX = domain.MaxX - domain.MinX;
        _widthY = domain.MaxY - domain.MinY;
        _widthZ = domain.Is3D ? domain.MaxZ - domain.MinZ : 0d;

        if (_widthX <= 0d || _widthY <= 0d || (domain.Is3D && _widthZ <= 0d))
        {
            throw new ArgumentException("Cartesian domains must have positive extents on all axes.", nameof(domain));
        }
    }

    public BoundingBox Normalize(BoundingBox box)
    {
        if (box.Dimensions != _domain.Dimensions)
        {
            throw new ArgumentException("Bounding box dimensionality must match the domain.", nameof(box));
        }

        if (box.Dimensions == 2)
        {
            return BoundingBox.From2D(
                NormalizeAxis(box.MinX, _domain.MinX, _widthX),
                NormalizeAxis(box.MinY, _domain.MinY, _widthY),
                NormalizeAxis(box.MaxX, _domain.MinX, _widthX),
                NormalizeAxis(box.MaxY, _domain.MinY, _widthY));
        }

        return BoundingBox.From3D(
            NormalizeAxis(box.MinX, _domain.MinX, _widthX),
            NormalizeAxis(box.MinY, _domain.MinY, _widthY),
            NormalizeAxis(box.MinZ, _domain.MinZ, _widthZ),
            NormalizeAxis(box.MaxX, _domain.MinX, _widthX),
            NormalizeAxis(box.MaxY, _domain.MinY, _widthY),
            NormalizeAxis(box.MaxZ, _domain.MinZ, _widthZ));
    }

    public (double x, double y) NormalizePoint2D(double x, double y)
    {
        return (
            NormalizeAxis(x, _domain.MinX, _widthX),
            NormalizeAxis(y, _domain.MinY, _widthY));
    }

    public (double x, double y, double z) NormalizePoint3D(double x, double y, double z)
    {
        if (!_domain.Is3D)
        {
            throw new InvalidOperationException("Domain is not configured for 3D coordinates.");
        }

        return (
            NormalizeAxis(x, _domain.MinX, _widthX),
            NormalizeAxis(y, _domain.MinY, _widthY),
            NormalizeAxis(z, _domain.MinZ, _widthZ));
    }

    private static double NormalizeAxis(double value, double min, double width)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("Coordinate components must be finite numbers.");
        }

        var normalized = (value - min) / width;

        if (normalized < 0d)
        {
            return 0d;
        }

        if (normalized > 1d)
        {
            return 1d;
        }

        return normalized;
    }
}
