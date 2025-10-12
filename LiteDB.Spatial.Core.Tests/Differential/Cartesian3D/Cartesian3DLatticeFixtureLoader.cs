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

    public static Cartesian3DLatticeFixture Load(string fileName = "cartesian3d_lattice_dense.json")
    {
        var path = Path.Combine(FixtureDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Cartesian3D lattice fixture '{fileName}' was not found.", path);
        }

        return Cartesian3DLatticeFixture.LoadFromFile(path);
    }
}
