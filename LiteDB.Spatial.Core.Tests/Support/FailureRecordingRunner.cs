using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using FsCheck;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

#nullable enable

namespace LiteDB.Spatial.Core.Tests.Support;

public sealed class FailureRecordingRunner : IRunner
{
    private readonly string _directory;
    private object[]? _lastArguments;

    public FailureRecordingRunner(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public void OnStartFixture(Type t)
    {
    }

    public void OnArguments(int n, FSharpList<object> args, FSharpFunc<int, FSharpFunc<FSharpList<object>, string>> every)
    {
        _lastArguments = args?.ToArray();
    }

    public void OnShrink(FSharpList<object> args, FSharpFunc<FSharpList<object>, string> everyShrink)
    {
        _lastArguments = args?.ToArray();
    }

    public void OnFinished(string name, TestResult result)
    {
        if (!result.IsFalse && !result.IsExhausted)
        {
            return;
        }

        var payload = new FailurePayload
        {
            Test = name,
            Outcome = result.ToString(),
            Arguments = _lastArguments?.Select(SerializeArgument).ToArray() ?? Array.Empty<JsonElement>(),
            Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };

        var fileName = $"{Sanitize(name)}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json";
        var path = Path.Combine(_directory, fileName);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(path, json);
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static JsonElement SerializeArgument(object argument)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(argument, argument?.GetType() ?? typeof(object));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed record FailurePayload
    {
        public string Test { get; init; } = string.Empty;
        public string Outcome { get; init; } = string.Empty;
        public string Timestamp { get; init; } = string.Empty;
        public IReadOnlyList<JsonElement> Arguments { get; init; } = Array.Empty<JsonElement>();
    }
}
