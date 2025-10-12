using LiteDB.Spatial.Testing.Oracles.Abstractions;

namespace LiteDB.Spatial.Core.Tests.Infrastructure.Models;

internal sealed record GeodesicPairFixture(
    string Id,
    GeoCoordinate Start,
    GeoCoordinate End,
    double ExpectedMeters);
