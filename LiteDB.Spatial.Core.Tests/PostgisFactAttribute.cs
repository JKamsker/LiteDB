#nullable enable

using System;
using Xunit;

namespace LiteDB.Spatial.Core.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PostgisFactAttribute : FactAttribute
{
    public PostgisFactAttribute()
    {
        var toggle = Environment.GetEnvironmentVariable("SPATIAL_DB_TESTS");
        if (string.IsNullOrWhiteSpace(toggle))
        {
            Skip = "Set SPATIAL_DB_TESTS (and POSTGIS_CONNECTION_STRING) to enable PostGIS-backed comparisons.";
        }
    }
}
