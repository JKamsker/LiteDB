#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Represents configuration options that govern how spatial indexes are generated and queried.
/// </summary>
public sealed class SpatialIndexOptions : IEquatable<SpatialIndexOptions>
{
    /// <summary>
    /// The default name for the numeric index field stored on documents.
    /// </summary>
    public const string DefaultIndexFieldName = "_idx";

    /// <summary>
    /// The default name for the bounding box field stored on documents.
    /// </summary>
    public const string DefaultBoundingBoxFieldName = "_mbb";

    /// <summary>
    /// The default precision in bits applied to each coordinate axis.
    /// </summary>
    public const int DefaultPrecision = 32;

    /// <summary>
    /// The default maximum number of cells used for index coverings.
    /// </summary>
    public const int DefaultMaxCoveringCells = 64;

    /// <summary>
    /// The default distance tolerance applied to near queries.
    /// </summary>
    public const double DefaultDistanceTolerance = 0.001d;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialIndexOptions"/> class.
    /// </summary>
    /// <param name="precisionBits">Number of bits used by the index encoder to represent each axis.</param>
    /// <param name="maxCoveringCells">The maximum number of index ranges allowed when covering a shape.</param>
    /// <param name="distanceTolerance">Additional expansion applied to distance queries to accommodate floating-point error.</param>
    /// <param name="indexFieldName">The field used to store the encoded index value on documents.</param>
    /// <param name="boundingBoxFieldName">The field used to store the bounding box on documents.</param>
    public SpatialIndexOptions(
        int precisionBits = DefaultPrecision,
        int maxCoveringCells = DefaultMaxCoveringCells,
        double distanceTolerance = DefaultDistanceTolerance,
        string indexFieldName = DefaultIndexFieldName,
        string boundingBoxFieldName = DefaultBoundingBoxFieldName)
    {
        if (precisionBits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(precisionBits), "Precision must be a positive number of bits.");
        }

        if (maxCoveringCells <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCoveringCells), "The maximum number of covering cells must be positive.");
        }

        if (distanceTolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(distanceTolerance), "Distance tolerance cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(indexFieldName))
        {
            throw new ArgumentException("Index field name cannot be null or whitespace.", nameof(indexFieldName));
        }

        if (string.IsNullOrWhiteSpace(boundingBoxFieldName))
        {
            throw new ArgumentException("Bounding box field name cannot be null or whitespace.", nameof(boundingBoxFieldName));
        }

        PrecisionBits = precisionBits;
        MaxCoveringCells = maxCoveringCells;
        DistanceTolerance = distanceTolerance;
        IndexFieldName = indexFieldName;
        BoundingBoxFieldName = boundingBoxFieldName;
    }

    /// <summary>
    /// Gets the number of bits allocated to represent each coordinate axis.
    /// </summary>
    public int PrecisionBits { get; }

    /// <summary>
    /// Gets the maximum number of index cells that planners may request when approximating shapes.
    /// </summary>
    public int MaxCoveringCells { get; }

    /// <summary>
    /// Gets the amount of tolerance applied to distance-based operations.
    /// </summary>
    public double DistanceTolerance { get; }

    /// <summary>
    /// Gets the document field used to store encoded index values.
    /// </summary>
    public string IndexFieldName { get; }

    /// <summary>
    /// Gets the document field used to store bounding boxes.
    /// </summary>
    public string BoundingBoxFieldName { get; }

    /// <summary>
    /// Creates a new <see cref="SpatialIndexOptions"/> instance using the existing values as defaults.
    /// </summary>
    public SpatialIndexOptions With(
        int? precisionBits = null,
        int? maxCoveringCells = null,
        double? distanceTolerance = null,
        string? indexFieldName = null,
        string? boundingBoxFieldName = null)
    {
        return new SpatialIndexOptions(
            precisionBits ?? PrecisionBits,
            maxCoveringCells ?? MaxCoveringCells,
            distanceTolerance ?? DistanceTolerance,
            indexFieldName ?? IndexFieldName,
            boundingBoxFieldName ?? BoundingBoxFieldName);
    }

    /// <inheritdoc />
    public bool Equals(SpatialIndexOptions? other)
    {
        if (other is null)
        {
            return false;
        }

        return PrecisionBits == other.PrecisionBits
            && MaxCoveringCells == other.MaxCoveringCells
            && DistanceTolerance.Equals(other.DistanceTolerance)
            && string.Equals(IndexFieldName, other.IndexFieldName, StringComparison.Ordinal)
            && string.Equals(BoundingBoxFieldName, other.BoundingBoxFieldName, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is SpatialIndexOptions options && Equals(options);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = PrecisionBits;
            hash = (hash * 397) ^ MaxCoveringCells;
            hash = (hash * 397) ^ DistanceTolerance.GetHashCode();
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(IndexFieldName);
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(BoundingBoxFieldName);
            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Precision={PrecisionBits}, Cells={MaxCoveringCells}, Tolerance={DistanceTolerance}, IndexField=\"{IndexFieldName}\", BoundingField=\"{BoundingBoxFieldName}\"";
    }
}
