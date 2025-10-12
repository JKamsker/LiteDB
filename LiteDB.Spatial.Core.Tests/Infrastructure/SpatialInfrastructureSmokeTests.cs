using System.Linq;
using FluentAssertions;
using LiteDB.Spatial.Core.Tests.Infrastructure;
using LiteDB.Spatial.Testing.Oracles;
using LiteDB.Spatial.Testing.Oracles.Abstractions;
using Xunit;
using OracleGeoCoordinate = LiteDB.Spatial.Testing.Oracles.Abstractions.GeoCoordinate;

namespace LiteDB.Spatial.Core.Tests.Infrastructure
{
    public sealed class SpatialInfrastructureSmokeTests
    {
        [Fact]
        public void Geodesic_fixture_pack_is_available()
        {
            var pairs = SpatialFixtureLoader.LoadGeodesicPairs();
            pairs.Should().NotBeEmpty("baseline geodesic fixtures should exist");
        }

        [Fact]
        public void Polygon_fixture_pack_is_available()
        {
            var polygons = SpatialFixtureLoader.LoadPolygons();
            polygons.Should().NotBeEmpty("baseline polygon fixtures should exist");
        }

        [Fact]
        public void Point_cloud_fixture_pack_is_available()
        {
            var pointClouds = SpatialFixtureLoader.LoadPointClouds();
            pointClouds.Should().NotBeEmpty("baseline point cloud fixtures should exist");
        }

        [Fact]
        public void Nts_oracle_matches_expected_area_for_unit_square()
        {
            var polygon = SpatialFixtureLoader.LoadPolygons().First(f => f.Id == "unit-square");
            var oracle = SpatialOracleCatalog.Geometry2D.First();

            if (!SpatialOracleCatalog.ShouldExecute(oracle))
            {
                return;
            }

            var area = oracle.ComputeArea(polygon.Polygon);
            SpatialTolerance.AssertWithinCartesianDistance(polygon.Id, 1d, area, "area");
        }

        [Fact]
        public void Geographic_oracle_returns_distance_for_fixture()
        {
            var pair = SpatialFixtureLoader.LoadGeodesicPairs().First();
            var oracle = SpatialOracleCatalog.Geodesic.First();

            if (!SpatialOracleCatalog.ShouldExecute(oracle))
            {
                return;
            }

            var start = new OracleGeoCoordinate(pair.From.Latitude, pair.From.Longitude);
            var end = new OracleGeoCoordinate(pair.To.Latitude, pair.To.Longitude);
            var distance = oracle.DistanceMeters(start, end);

            distance.Should().BePositive();
            SpatialTolerance.AssertWithinEarthDistance(pair.Id, distance, distance, oracle.Name);
        }
    }
}
