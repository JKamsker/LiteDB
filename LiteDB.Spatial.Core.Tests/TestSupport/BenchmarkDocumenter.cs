#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class BenchmarkDocumenter
{
    private const string StartMarker = "<!-- benchmark:begin -->";
    private const string EndMarker = "<!-- benchmark:end -->";

    public static void PersistResults(IReadOnlyList<BenchmarkResult> results)
    {
        if (results == null || results.Count == 0)
        {
            return;
        }

        var path = PerfTestEnvironment.GetBenchmarksDocumentPath();
        var content = File.Exists(path) ? File.ReadAllText(path) : CreateDefaultDocument();
        var summary = BuildSummary(results);
        var updated = ReplaceSection(content, summary);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, updated, Encoding.UTF8);
    }

    private static string ReplaceSection(string content, string summary)
    {
        var startIndex = content.IndexOf(StartMarker, StringComparison.Ordinal);
        var endIndex = content.IndexOf(EndMarker, StringComparison.Ordinal);

        if (startIndex < 0 || endIndex < 0 || endIndex < startIndex)
        {
            return content + Environment.NewLine + StartMarker + Environment.NewLine + summary + Environment.NewLine + EndMarker + Environment.NewLine;
        }

        startIndex += StartMarker.Length;
        return content.Substring(0, startIndex) + Environment.NewLine + summary + Environment.NewLine + content.Substring(endIndex);
    }

    private static string BuildSummary(IReadOnlyList<BenchmarkResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Last updated: {DateTime.UtcNow:u}");
        builder.AppendLine();
        builder.AppendLine("| Engine | Dataset | Query | Candidates | Index Hits | Reduction | Duration (ms) | Rows | Notes |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");

        foreach (var result in results.OrderBy(r => r.Dataset).ThenBy(r => r.Engine))
        {
            var reduction = result.ReductionRatio.HasValue
                ? result.ReductionRatio.Value.ToString("P1", CultureInfo.InvariantCulture)
                : "-";
            var indexHits = result.IndexCandidates.HasValue ? result.IndexCandidates.Value.ToString(CultureInfo.InvariantCulture) : "-";
            var notes = string.IsNullOrWhiteSpace(result.Notes) ? "" : result.Notes!.Replace("\n", "<br/>");

            builder.Append("| ")
                .Append(result.Engine)
                .Append(" | ")
                .Append(result.Dataset)
                .Append(" | ")
                .Append(result.Query)
                .Append(" | ")
                .Append(result.TotalCandidates.ToString(CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(indexHits)
                .Append(" | ")
                .Append(reduction)
                .Append(" | ")
                .Append(result.Duration.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(result.RowsReturned.ToString(CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(notes)
                .AppendLine(" |");
        }

        return builder.ToString().TrimEnd();
    }

    private static string CreateDefaultDocument()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Spatial Benchmarks");
        builder.AppendLine();
        builder.AppendLine("This file is automatically updated by the spatial performance harness.");
        builder.AppendLine();
        builder.AppendLine(StartMarker);
        builder.AppendLine("No benchmark results captured yet.");
        builder.AppendLine(EndMarker);
        builder.AppendLine();
        builder.AppendLine("Refer to the repository documentation for instructions on running the harness.");
        return builder.ToString();
    }
}

internal sealed record BenchmarkResult(
    string Engine,
    string Dataset,
    string Query,
    int TotalCandidates,
    int RowsReturned,
    TimeSpan Duration,
    int? IndexCandidates = null,
    double? ReductionRatio = null,
    string? Notes = null);
