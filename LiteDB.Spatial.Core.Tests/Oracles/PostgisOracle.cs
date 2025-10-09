using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Npgsql;

#nullable enable

namespace LiteDB.Spatial.Core.Tests.Oracles;

public static class PostgisOracle
{
    public static bool IsEnabled => string.Equals(Environment.GetEnvironmentVariable("SPATIAL_DB_TESTS"), "1", StringComparison.Ordinal);

    public static bool TryDWithin(
        IEnumerable<NearFixturePoint> points,
        GeoPoint center,
        double radiusMeters,
        out IReadOnlyList<int> matches,
        out string? skipReason)
    {
        matches = Array.Empty<int>();
        skipReason = null;

        if (!IsEnabled)
        {
            skipReason = "Set SPATIAL_DB_TESTS=1 to enable PostGIS-backed comparisons.";
            return false;
        }

        var connectionString = Environment.GetEnvironmentVariable("POSTGIS_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            skipReason = "POSTGIS_CONNECTION environment variable is not configured.";
            return false;
        }

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();

            var values = string.Join(", ", points.Select(p =>
                FormattableString.Invariant($"({p.Id}, ST_SetSRID(ST_MakePoint({p.Lon}, {p.Lat}), 4326))")));

            if (string.IsNullOrEmpty(values))
            {
                matches = Array.Empty<int>();
                return true;
            }

            var sql = FormattableString.Invariant($@"
                SELECT id
                FROM (VALUES {values}) AS input(id, geom)
                WHERE ST_DWithin(
                    geom::geography,
                    ST_SetSRID(ST_MakePoint({center.Longitude}, {center.Latitude}), 4326)::geography,
                    {radiusMeters}
                )
                ORDER BY id;
            ");

            using var command = new NpgsqlCommand(sql, connection);
            using var reader = command.ExecuteReader();
            var result = new List<int>();
            while (reader.Read())
            {
                result.Add(reader.GetInt32(0));
            }

            matches = result;
            return true;
        }
        catch (Exception ex)
        {
            skipReason = $"PostGIS query failed: {ex.Message}";
            return false;
        }
    }
}
