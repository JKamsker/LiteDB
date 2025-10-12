using FluentAssertions;
using FluentAssertions.Execution;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.Infrastructure;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine.Differential;

// Regenerate fixtures with:
//   pip install geographiclib
//   python - <<'PY'
//   from geographiclib.geodesic import Geodesic
//   import json
//   pairs = json.load(open('LiteDB.Spatial.Core.Tests/fixtures/geodesic_pairs.json'))
//   for pair in pairs:
//       res = Geodesic.WGS84.Inverse(pair["from"]["lat"], pair["from"]["lon"], pair["to"]["lat"], pair["to"]["lon"])
//       pair["geographicLibDistanceMeters"] = res["s12"]
//   json.dump(pairs, open('LiteDB.Spatial.Core.Tests/fixtures/geodesic_pairs.json', 'w'), indent=2)
//   PY

[Category("geographic")]
[Category("oracle")]
public sealed class GeodesicDistanceParityTests
{
    [Fact]
    public void HaversineMatchesGeographicLibSamples()
    {
        var distance = new GeographicDistance(GeographicDistanceMode.Haversine);
        foreach (var pair in GeographicLibOracle.LoadPairs())
        {
            using var scope = new AssertionScope();
            scope.AddReportable("fixture", pair.Id);
            scope.AddReportable("mode", nameof(GeographicDistanceMode.Haversine));

            var expected = GeographicLibOracle.Distance(pair.Id, pair.From, pair.To);
            var actual = distance.Distance(pair.From, pair.To);
            SpatialTolerance.AssertEarthDistance(pair.Id, expected, actual);
        }
    }

    [Fact]
    public void VincentyMatchesGeographicLibSamples()
    {
        var distance = new GeographicDistance(GeographicDistanceMode.Vincenty);
        foreach (var pair in GeographicLibOracle.LoadPairs())
        {
            using var scope = new AssertionScope();
            scope.AddReportable("fixture", pair.Id);
            scope.AddReportable("mode", nameof(GeographicDistanceMode.Vincenty));

            var expected = GeographicLibOracle.Distance(pair.Id, pair.From, pair.To);
            var actual = distance.Distance(pair.From, pair.To);
            SpatialTolerance.AssertEarthDistance(pair.Id, expected, actual);
        }
    }
}
