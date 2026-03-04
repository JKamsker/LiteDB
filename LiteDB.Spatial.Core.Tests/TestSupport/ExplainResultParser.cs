using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class ExplainResultParser
{
    public static IReadOnlyDictionary<string, string> ParseDiagnostics(SpatialExplainResult explain)
    {
        if (explain == null)
        {
            throw new ArgumentNullException(nameof(explain));
        }

        var line = explain
            .ToString()
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(text => text.StartsWith("Covering diagnostics:", StringComparison.Ordinal));

        if (line == null)
        {
            return new Dictionary<string, string>();
        }

        var prefixLength = "Covering diagnostics:".Length;
        var payload = line.Substring(prefixLength).Trim();
        var segments = payload.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in segments)
        {
            var parts = segment.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            map[parts[0].Trim()] = parts[1].Trim();
        }

        return map;
    }

    public static ulong ParseRangeCount(IReadOnlyDictionary<string, string> diagnostics, string key)
    {
        if (!diagnostics.TryGetValue(key, out var value))
        {
            return 0UL;
        }

        if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0UL;
    }
}
