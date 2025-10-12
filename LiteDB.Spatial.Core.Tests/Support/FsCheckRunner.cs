using System;
using System.IO;
using FsCheck;
using Microsoft.FSharp.Core;

namespace LiteDB.Spatial.Core.Tests.Support;

public static class FsCheckRunner
{
    private static readonly string FailuresDirectory = Path.Combine("tests", "failures");

    public static void Check(string name, Property property, int maxTest = 200)
    {
        Directory.CreateDirectory(FailuresDirectory);
        var baseConfig = Config.QuickThrowOnFailure;
        var replay = ParseReplay();
        var effectiveReplay = FSharpOption<FsCheck.Random.StdGen>.get_IsSome(replay)
            ? replay
            : baseConfig.Replay;

        var config = new Config(
            maxTest,
            baseConfig.MaxFail,
            effectiveReplay,
            baseConfig.Name,
            baseConfig.StartSize,
            baseConfig.EndSize,
            baseConfig.QuietOnSuccess,
            baseConfig.Every,
            baseConfig.EveryShrink,
            baseConfig.Arbitrary,
            new FailureRecordingRunner(FailuresDirectory));

        FsCheck.Check.One(config, property.Label(name));
    }

    private static FSharpOption<FsCheck.Random.StdGen> ParseReplay()
    {
        var value = Environment.GetEnvironmentVariable("FS_CHECK_SEED");
        if (string.IsNullOrWhiteSpace(value))
        {
            return FSharpOption<FsCheck.Random.StdGen>.None;
        }

        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return FSharpOption<FsCheck.Random.StdGen>.None;
        }

        if (!int.TryParse(parts[0], out var seed))
        {
            return FSharpOption<FsCheck.Random.StdGen>.None;
        }

        var size = parts.Length > 1 && int.TryParse(parts[1], out var parsedSize) ? parsedSize : 0;
        var stdGen = FsCheck.Random.StdGen.NewStdGen(seed, size);
        return FSharpOption<FsCheck.Random.StdGen>.Some(stdGen);
    }
}
