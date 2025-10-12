using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LiteDB.Spatial.Testing.Oracles.Internal
{
    internal static class OracleEnvironment
    {
        private const string OraclesVariable = "SPATIAL_ORACLES";
        private const string DbTestsVariable = "SPATIAL_DB_TESTS";

        private static readonly HashSet<string> AllOracles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            OracleNames.Nts,
            OracleNames.GeographicLib,
            OracleNames.MathNet3D,
            OracleNames.Postgis
        };

        public static bool RequiresOracle(string oracleName)
        {
            var normalized = Normalize(oracleName);
            if (normalized is null)
            {
                return false;
            }

            var value = Environment.GetEnvironmentVariable(OraclesVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            var tokens = value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var normalizedTokens = tokens
                .Select(static t => Normalize(t))
                .Where(static t => t is not null)
                .Select(static t => t!)
                .ToArray();

            if (normalizedTokens.Length == 0)
            {
                return true;
            }

            if (normalizedTokens.Any(static t => string.Equals(t, "*", StringComparison.Ordinal)))
            {
                return true;
            }

            return normalizedTokens.Contains(normalized);
        }

        public static bool AreDatabaseTestsEnabled()
        {
            var value = Environment.GetEnvironmentVariable(DbTestsVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            value = value.Trim();
            if (string.Equals(value, "1", StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(value, "0", StringComparison.Ordinal))
            {
                return false;
            }

            if (bool.TryParse(value, out var result))
            {
                return result;
            }

            return string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "y", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsKnownOracle(string? name)
        {
            if (name is null)
            {
                return false;
            }

            return AllOracles.Contains(name);
        }

        public static string? Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim().ToLower(CultureInfo.InvariantCulture);
        }
    }
}
