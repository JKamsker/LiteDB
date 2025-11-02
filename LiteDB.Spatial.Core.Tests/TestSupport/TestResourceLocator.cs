using System;
using System.IO;
using System.Linq;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class TestResourceLocator
{
    private static readonly Lazy<string> RepositoryRootAccessor = new(LocateRepositoryRoot, isThreadSafe: true);

    public static string RepositoryRoot => RepositoryRootAccessor.Value;

    public static string GetFailuresDirectory()
    {
        var directory = Path.Combine(RepositoryRoot, "tests", "failures");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string GetFixturePath(params string[] segments)
    {
        var path = Path.Combine(new[] { RepositoryRoot, "tests", "fixtures" }.Concat(segments).ToArray());
        return path;
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LiteDB.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Unable to locate repository root from '{AppContext.BaseDirectory}'.");
    }
}
