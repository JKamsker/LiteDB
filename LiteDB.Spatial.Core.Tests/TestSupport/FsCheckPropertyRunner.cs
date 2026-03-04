#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using FsCheck;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Reflection;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class FsCheckPropertyRunner
{
    private const string SeedEnvironmentVariable = "SPATIAL_FSCHECK_SEED";

    public static void Check(string propertyName, Property property)
    {
        if (propertyName == null)
        {
            throw new ArgumentNullException(nameof(propertyName));
        }

        if (property == null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        var config = Config.Quick
            .WithName(propertyName)
            .WithRunner(new PersistingRunner(propertyName))
            .WithMaxTest(200)
            .WithStartSize(1)
            .WithEndSize(200);

        if (TryReadSeed(out var seed, out var gamma, out var size))
        {
            config = config.WithReplay(seed, gamma, size ?? -1);
        }

        FsCheck.Check.One(config, property);
    }

    private static bool TryReadSeed(out ulong seed, out ulong gamma, out int? size)
    {
        seed = 0;
        gamma = 0;
        size = null;

        var value = Environment.GetEnvironmentVariable(SeedEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Length > 3)
        {
            throw new InvalidOperationException($"Invalid seed format '{value}'. Expected 'seed:gamma[:size]'.");
        }

        seed = ulong.Parse(parts[0], CultureInfo.InvariantCulture);
        gamma = ulong.Parse(parts[1], CultureInfo.InvariantCulture);
        if (parts.Length == 3)
        {
            size = int.Parse(parts[2], CultureInfo.InvariantCulture);
        }

        return true;
    }

    private sealed class PersistingRunner : IRunner
    {
        private static readonly JsonSerializerOptions _payloadSerializer = new()
        {
            WriteIndented = true
        };

        private static readonly JsonSerializerOptions _argumentSerializer = new()
        {
            WriteIndented = false
        };

        private readonly string _propertyName;
        private readonly string _failureDirectory;

        public PersistingRunner(string propertyName)
        {
            _propertyName = propertyName;
            _failureDirectory = TestResourceLocator.GetFailuresDirectory();
        }

        public void OnStartFixture(Type t)
        {
            // Intentionally ignored to keep test output quiet.
        }

        public void OnArguments(int n, FSharpList<object> args, Microsoft.FSharp.Core.FSharpFunc<int, Microsoft.FSharp.Core.FSharpFunc<FSharpList<object>, string>> every)
        {
            // No-op: xUnit already surfaces failures with the persisted payload.
        }

        public void OnShrink(FSharpList<object> args, Microsoft.FSharp.Core.FSharpFunc<FSharpList<object>, string> everyShrink)
        {
            // No-op
        }

        public void OnFinished(string name, TestResult result)
        {
            var (caseInfo, fields) = FSharpValue.GetUnionFields(result, typeof(TestResult), null);
            if (!string.Equals(caseInfo.Name, "Failed", StringComparison.Ordinal))
            {
                return;
            }

            Directory.CreateDirectory(_failureDirectory);

            var originalArgs = FormatArguments((FSharpList<object>)fields[1]);
            var shrunkArgs = FormatArguments((FSharpList<object>)fields[2]);
            var initialSeed = fields.Length > 4 ? fields[4]?.ToString() : null;
            var failingSeed = fields.Length > 5 ? fields[5]?.ToString() : null;
            var failingSize = fields.Length > 6 ? (int?)fields[6] : null;
            var summary = Runner.onFinishedToString(name, result);

            var payload = new FailurePayload
            {
                Property = _propertyName,
                Test = name,
                Summary = summary,
                OriginalArguments = originalArgs,
                ShrunkArguments = shrunkArgs,
                InitialSeed = initialSeed,
                FailureSeed = failingSeed,
                FailureSize = failingSize,
                Timestamp = DateTimeOffset.UtcNow
            };

            var fileName = $"{_propertyName}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.json";
            var path = Path.Combine(_failureDirectory, fileName);
            File.WriteAllText(path, JsonSerializer.Serialize(payload, _payloadSerializer));
        }

        private static string[] FormatArguments(FSharpList<object> arguments)
        {
            var values = ListModule.ToArray(arguments);
            var formatted = new string[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                formatted[i] = FormatSingleArgument(values[i]);
            }

            return formatted;
        }

        private static string FormatSingleArgument(object? argument)
        {
            if (argument == null)
            {
                return "null";
            }

            var type = argument.GetType();
            try
            {
                return JsonSerializer.Serialize(argument, type, _argumentSerializer);
            }
            catch
            {
                return argument.ToString() ?? type.FullName ?? "<unserializable>";
            }
        }

        private sealed record FailurePayload
        {
            public required string Property { get; init; }

            public required string? Test { get; init; }

            public required string Summary { get; init; }

            public required IReadOnlyList<string> OriginalArguments { get; init; }

            public required IReadOnlyList<string> ShrunkArguments { get; init; }

            public string? InitialSeed { get; init; }

            public string? FailureSeed { get; init; }

            public int? FailureSize { get; init; }

            public DateTimeOffset Timestamp { get; init; }
        }
    }
}
