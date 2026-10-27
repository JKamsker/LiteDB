using LiteDB;

namespace Ab5021Benchmarks;

internal static class Settings
{
    public static object Describe(string config) => new
    {
        durableCommits = false,
        compactStorage = config.Contains("legacy") ? "Legacy" : "Auto",
        transactionPages = config.Contains("pages") ? 100000 : config.Contains("throughput") ? 4000 : 1000,
        cacheBytes = (config.Contains("cache") || config.Contains("throughput") ? 128L : 64L) * 1024 * 1024,
        checkpointPages = config.EndsWith("no-checkpoint") ? 0 : 1000,
        note = config.StartsWith("v5") ? "v5 native defaults; v6-only settings inapplicable" : "explicit research controls"
    };

#if DEV
    public static void Configure(ConnectionString connection, string config)
    {
        connection.DurableCommits = false;
        if (config.Contains("legacy")) connection.CompactStorage = CompactStorageMode.Legacy;
        if (config.Contains("throughput")) connection.MemoryProfile = MemoryProfile.Throughput;
        if (config.Contains("cache")) connection.CacheSize = 128L * 1024 * 1024;
        if (config.Contains("pages")) connection.TransactionPageLimit = 100000;
    }
#endif
}
