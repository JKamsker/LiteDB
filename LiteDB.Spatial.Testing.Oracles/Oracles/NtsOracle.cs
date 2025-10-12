using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial.Testing.Oracles.Abstractions;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace LiteDB.Spatial.Testing.Oracles.Oracles;

public sealed class NtsOracle : OracleBase, IGeometryOracle2D
{
    private readonly GeometryFactory? _factory;
    private readonly GeoJsonReader? _reader;

    public NtsOracle()
        : base("nts", isDatabaseOracle: false)
    {
        if (!IsAvailable)
        {
            return;
        }

        _factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
        _reader = new GeoJsonReader();
    }

    public double GetAreaSquareMeters(IEnumerable<IReadOnlyList<GeoPoint2D>> rings, bool areRingsClosed = true)
    {
        EnsureAvailable();
        return BuildPolygon(rings, areRingsClosed).Area;
    }

    public double GetPerimeterMeters(IEnumerable<IReadOnlyList<GeoPoint2D>> rings, bool areRingsClosed = true)
    {
        EnsureAvailable();
        var polygon = BuildPolygon(rings, areRingsClosed);
        var perimeter = polygon.ExteriorRing.Length;
        foreach (var interior in polygon.InteriorRings)
        {
            perimeter += interior.Length;
        }

        return perimeter;
    }

    public Geometry ParseGeoJson(string geoJson)
    {
        EnsureAvailable();
        var reader = _reader ?? throw new InvalidOperationException("NTS GeoJSON reader is not initialised");
        return reader.Read<Geometry>(geoJson);
    }

    private Polygon BuildPolygon(IEnumerable<IReadOnlyList<GeoPoint2D>> rings, bool areRingsClosed)
    {
        var ringList = rings.ToList();
        if (ringList.Count == 0)
        {
            throw new ArgumentException("At least one ring is required to build a polygon", nameof(rings));
        }

        var factory = _factory ?? throw new InvalidOperationException("NTS geometry factory is not initialised");
        var shell = CreateLinearRing(ringList[0], areRingsClosed, factory);
        var holes = ringList.Skip(1).Select(ring => CreateLinearRing(ring, areRingsClosed, factory)).ToArray();
        return factory.CreatePolygon(shell, holes);
    }

    private LinearRing CreateLinearRing(IReadOnlyList<GeoPoint2D> ring, bool isClosed, GeometryFactory factory)
    {
        var coordinates = (isClosed ? ring : ring.CloseRing())
            .Select(p => new Coordinate(p.X, p.Y))
            .ToArray();

        if (coordinates.Length < 4)
        {
            throw new ArgumentException("A linear ring requires at least 4 coordinates (including closing point)", nameof(ring));
        }

        return factory.CreateLinearRing(coordinates);
    }

    private void EnsureAvailable()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(SkipReason ?? "NTS oracle is disabled");
        }
    }
}
