using System.Collections.Generic;
using OracleGeoPoint3D = LiteDB.Spatial.Testing.Oracles.Abstractions.GeoPoint3D;

namespace LiteDB.Spatial.Core.Tests.Infrastructure.Models;

internal sealed record PointCloudFixture(
    string Id,
    IReadOnlyList<OracleGeoPoint3D> Points);
