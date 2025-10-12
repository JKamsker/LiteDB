using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian3D;

internal static class Cartesian3DLatticeFixtureLoader
{
    private static readonly string FixtureDirectory = TestPathHelper.GetPath("LiteDB.Spatial.Core.Tests", "Differential", "Cartesian3D", "Fixtures");

    public static IEnumerable<Cartesian3DLatticeFixture> LoadAll()
    {
        if (!Directory.Exists(FixtureDirectory))
        {
            return Enumerable.Empty<Cartesian3DLatticeFixture>();
        }

        return Directory
            .EnumerateFiles(FixtureDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path)
            .Select(Cartesian3DLatticeFixture.LoadFromFile)
            .ToList();
    }

    public static IEnumerable<object[]> FixtureData()
    {
        return LoadAll().Select(fixture => new object[] { fixture });
    }
}
