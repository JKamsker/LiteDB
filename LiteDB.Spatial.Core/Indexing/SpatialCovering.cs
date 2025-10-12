#nullable enable

using System;
using System.Collections.Generic;

namespace LiteDB.Spatial;

/// <summary>
/// Represents the Morton covering generated for a spatial query.
/// </summary>
public sealed class SpatialCovering
{
    private static readonly IReadOnlyList<SpatialIndexRange> EmptyRanges = Array.Empty<SpatialIndexRange>();

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialCovering"/> class.
    /// </summary>
    /// <param name="ranges">The index ranges produced by the encoder.</param>
    /// <param name="diagnostics">Diagnostics describing how the covering was generated.</param>
    public SpatialCovering(IReadOnlyList<SpatialIndexRange> ranges, SpatialCoveringDiagnostics diagnostics)
    {
        Ranges = ranges ?? throw new ArgumentNullException(nameof(ranges));
        Diagnostics = diagnostics;
    }

    /// <summary>
    /// Gets an empty covering result.
    /// </summary>
    public static SpatialCovering Empty { get; } = new SpatialCovering(EmptyRanges, SpatialCoveringDiagnostics.Empty);

    /// <summary>
    /// Gets the index ranges produced by the encoder.
    /// </summary>
    public IReadOnlyList<SpatialIndexRange> Ranges { get; }

    /// <summary>
    /// Gets diagnostics describing how the covering was generated.
    /// </summary>
    public SpatialCoveringDiagnostics Diagnostics { get; }
}
