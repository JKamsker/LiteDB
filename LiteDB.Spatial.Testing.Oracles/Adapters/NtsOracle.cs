using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial.Testing.Oracles.Abstractions;
using NetTopologySuite.Geometries;
using GeoLineString = GeoJSON.Net.Geometry.LineString;
using GeoPolygon = GeoJSON.Net.Geometry.Polygon;

namespace LiteDB.Spatial.Testing.Oracles.Adapters
{
    public sealed class NtsOracle : IGeometryOracle2D
    {
        private readonly GeometryFactory _geometryFactory;

        public NtsOracle()
        {
            _geometryFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
        }

        public string Name => OracleNames.Nts;

        public bool IsAvailable => true;

        public double ComputeArea(GeoPolygon polygon)
        {
            if (polygon is null)
            {
                throw new ArgumentNullException(nameof(polygon));
            }

            var ntsPolygon = CreatePolygon(polygon);
            return ntsPolygon.Area;
        }

        public double ComputePerimeter(GeoPolygon polygon)
        {
            if (polygon is null)
            {
                throw new ArgumentNullException(nameof(polygon));
            }

            var ntsPolygon = CreatePolygon(polygon);
            return ntsPolygon.Length;
        }

        private NetTopologySuite.Geometries.Polygon CreatePolygon(GeoPolygon polygon)
        {
            if (polygon.Coordinates is null || polygon.Coordinates.Count == 0)
            {
                throw new ArgumentException("Polygon must provide at least one linear ring.", nameof(polygon));
            }

            var shell = CreateLinearRing(polygon.Coordinates[0]);
            var holes = polygon.Coordinates.Skip(1).Select(CreateLinearRing).ToArray();
            return _geometryFactory.CreatePolygon(shell, holes);
        }

        private LinearRing CreateLinearRing(GeoLineString lineString)
        {
            if (lineString.Coordinates is null)
            {
                throw new ArgumentException("LineString must contain coordinates.", nameof(lineString));
            }

            var coordinates = new List<Coordinate>();
            foreach (var position in lineString.Coordinates)
            {
                coordinates.Add(new Coordinate(position.Longitude, position.Latitude));
            }

            if (coordinates.Count == 0)
            {
                throw new ArgumentException("Linear ring cannot be empty.", nameof(lineString));
            }

            if (!coordinates[0].Equals2D(coordinates[^1]))
            {
                coordinates.Add(coordinates[0]);
            }

            return _geometryFactory.CreateLinearRing(coordinates.ToArray());
        }
    }
}
