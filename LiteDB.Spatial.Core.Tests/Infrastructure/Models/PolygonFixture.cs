using System.Collections.Generic;
using LiteDB.Spatial.Testing.Oracles.Abstractions;

#nullable enable

namespace LiteDB.Spatial.Core.Tests.Infrastructure.Models;

internal sealed record PolygonFixture(
    string Id,
    string? Description,
    string GeoJson,
    IReadOnlyList<IReadOnlyList<GeoPoint2D>> Rings,
    double? ExpectedArea,
    double? ExpectedPerimeter);
