using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace LiteDB.Spatial.Core.Tests;

internal static class TestPathHelper
{
    private static readonly Lazy<string> RepositoryRootLazy = new(() => LocateRepositoryRoot(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static string RepositoryRoot => RepositoryRootLazy.Value;

    public static string GetPath(params string[] components)
    {
        if (components == null)
        {
            throw new ArgumentNullException(nameof(components));
        }

        return Path.Combine(new[] { RepositoryRoot }.Concat(components).ToArray());
    }

    private static string LocateRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;

        while (!string.IsNullOrEmpty(current))
        {
            var solutionPath = Path.Combine(current, "LiteDB.sln");
            if (File.Exists(solutionPath))
            {
                return current;
            }

            current = Path.GetDirectoryName(current)!;
        }

        throw new InvalidOperationException("Unable to locate the repository root from the test execution directory.");
    }
}
