#nullable enable

using System.Collections.Generic;
using LiteDB.Spatial;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal sealed record PointSample(int Id, GeoPoint Position);

internal sealed record NearQuery(string Name, GeoPoint Center, double Radius);

internal sealed record PointCloudFixture(
    string Name,
    BoundingBox Domain,
    IReadOnlyList<PointSample> Points,
    IReadOnlyList<NearQuery> Queries);
