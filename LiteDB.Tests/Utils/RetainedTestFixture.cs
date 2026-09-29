using System;
using System.Diagnostics;
using System.IO;
using Xunit.Abstractions;

namespace LiteDB.Tests
{
    internal static class RetainedTestFixture
    {
        // Publish only a manifest in the test host. Copying must wait until the
        // host exits; a failing finalizer assertion can leave live native handles.
        internal static void Publish(string filename, Exception primary, ITestOutputHelper output)
        {
            try
            {
                output.WriteLine("Retained graph fixture: {0}\n{1}", filename, primary);
                var directory = Environment.GetEnvironmentVariable("LITEDB_RETAINED_FIXTURES");
                if (string.IsNullOrEmpty(directory)) return;
                Directory.CreateDirectory(directory);
                var manifest = new BsonDocument
                {
                    ["database"] = Path.GetFullPath(filename),
                    ["processId"] = Process.GetCurrentProcess().Id,
                    ["failure"] = primary.ToString()
                };
                var target = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(target + ".tmp", JsonSerializer.Serialize(manifest));
                File.Move(target + ".tmp", target);
            }
            catch (Exception diagnostic)
            {
                // Diagnostics must never replace the original test failure.
                try { output.WriteLine("Fixture manifest publication failed: {0}", diagnostic); }
                catch { }
            }
        }
    }
}
