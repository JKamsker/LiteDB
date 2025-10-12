using System;
using System.Collections.Generic;

namespace LiteDB.Spatial;

/// <summary>
/// Provides the ability to encode coordinates into spatial index values and derive coarse coverings.
/// </summary>
public interface ISpatialIndexEncoder
{
    /// <summary>
    /// Gets the number of spatial dimensions supported by the encoder.
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Gets the number of precision bits allocated per axis.
    /// </summary>
    int PrecisionBits { get; }

    /// <summary>
    /// Encodes the provided coordinate tuple into a sortable spatial index value.
    /// </summary>
    /// <param name="coordinates">The coordinate tuple. The span length must match <see cref="Dimensions"/>.</param>
    /// <returns>The encoded index value.</returns>
    ulong Encode(ReadOnlySpan<double> coordinates);

    /// <summary>
    /// Produces a set of index ranges that cover the provided bounding box.
    /// </summary>
    /// <param name="bounds">The bounding box to cover.</param>
    /// <param name="maxCells">The maximum number of ranges the caller is willing to accept.</param>
    /// <returns>A covering description containing closed index ranges ordered from lowest to highest.</returns>
    SpatialCovering Cover(BoundingBox bounds, int maxCells);
}
