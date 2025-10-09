using System;
using FsCheck.Xunit;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal sealed class SpatialPropertyAttribute : PropertyAttribute
{
    public SpatialPropertyAttribute()
    {
        QuietOnSuccess = true;
        var seed = Environment.GetEnvironmentVariable("SPATIAL_FSCHECK_SEED");
        if (!string.IsNullOrWhiteSpace(seed))
        {
            Replay = seed;
        }
    }
}
