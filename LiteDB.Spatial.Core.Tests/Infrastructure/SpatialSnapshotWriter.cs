using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace LiteDB.Spatial.Core.Tests.Infrastructure
{
    public static class SpatialSnapshotWriter
    {
        private static readonly Lazy<string> SnapshotRoot = new Lazy<string>(ResolveSnapshotRoot);

        public static void WriteSnapshot(string category, string fixtureId, object payload)
        {
            try
            {
                var directory = Path.Combine(SnapshotRoot.Value, category);
                Directory.CreateDirectory(directory);

                var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture);
                var fileName = $"{Sanitize(fixtureId)}-{timestamp}.json";
                var path = Path.Combine(directory, fileName);

                var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception)
            {
                // Ignore snapshot IO errors to avoid masking the real assertion failure.
            }
        }

        private static string ResolveSnapshotRoot()
        {
            var baseDir = AppContext.BaseDirectory;
            var root = Path.Combine(baseDir, "..", "..", "..", "..", "TestResults", "SpatialSnapshots");
            return Path.GetFullPath(root);
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "fixture";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value;
        }
    }
}
