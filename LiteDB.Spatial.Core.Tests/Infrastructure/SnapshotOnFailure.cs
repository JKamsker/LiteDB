using System.IO;
using System.Text.Json;

namespace LiteDB.Spatial.Core.Tests.Infrastructure;

internal static class SnapshotOnFailure
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string WriteJson(string fixtureId, object payload)
    {
        var path = FixturePaths.MakeSnapshotPath(fixtureId, "json");
        var json = JsonSerializer.Serialize(payload, Options);
        File.WriteAllText(path, json);
        return path;
    }
}
