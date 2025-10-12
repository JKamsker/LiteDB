#nullable enable

using System;
using System.IO;
using System.Linq;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

/// <summary>
/// Resolves paths relative to the repository root for loading fixtures and persisting counterexamples.
/// </summary>
public static class RepositoryPath
{
    private static readonly Lazy<string> _root = new(LocateRoot, isThreadSafe: true);

    /// <summary>
    /// Gets the absolute path to the repository root.
    /// </summary>
    public static string Root => _root.Value;

    /// <summary>
    /// Combines the repository root with the provided path segments.
    /// </summary>
    public static string Combine(params string[] segments)
    {
        if (segments == null)
        {
            throw new ArgumentNullException(nameof(segments));
        }

        return Path.Combine(new[] { Root }.Concat(segments).ToArray());
    }

    private static string LocateRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "LiteDB.sln")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new InvalidOperationException("Unable to locate repository root from test base directory.");
    }
}
