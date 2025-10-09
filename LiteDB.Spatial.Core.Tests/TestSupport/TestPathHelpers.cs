#nullable enable

using System;
using System.IO;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class TestPathHelpers
{
    private static readonly Lazy<string> _repoRoot = new(() =>
    {
        // The tests run from bin/Debug/net*/; climb up to the repository root that hosts LiteDB.sln.
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "LiteDB.sln");
            if (File.Exists(candidate))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new InvalidOperationException("Unable to locate the repository root from the test base directory.");
    });

    public static string RepoRoot => _repoRoot.Value;

    public static string ResolveRelativeToRepo(params string[] segments)
    {
        return Path.Combine(RepoRoot, Path.Combine(segments));
    }
}
