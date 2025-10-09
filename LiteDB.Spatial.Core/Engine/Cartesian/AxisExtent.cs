#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Represents the valid extent of a Cartesian axis and provides normalization helpers.
/// </summary>
public readonly struct AxisExtent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AxisExtent"/> struct.
    /// </summary>
    /// <param name="minimum">Minimum supported value.</param>
    /// <param name="maximum">Maximum supported value.</param>
    public AxisExtent(double minimum, double maximum)
    {
        if (double.IsNaN(minimum) || double.IsInfinity(minimum))
        {
            throw new ArgumentException("Axis minimum must be a finite number.", nameof(minimum));
        }

        if (double.IsNaN(maximum) || double.IsInfinity(maximum))
        {
            throw new ArgumentException("Axis maximum must be a finite number.", nameof(maximum));
        }

        if (maximum <= minimum)
        {
            throw new ArgumentException("Axis maximum must be greater than the minimum.", nameof(maximum));
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>
    /// Gets the minimum supported value.
    /// </summary>
    public double Minimum { get; }

    /// <summary>
    /// Gets the maximum supported value.
    /// </summary>
    public double Maximum { get; }

    /// <summary>
    /// Gets the span covered by the extent.
    /// </summary>
    public double Span => Maximum - Minimum;

    /// <summary>
    /// Normalizes the provided coordinate into the unit interval.
    /// </summary>
    /// <param name="value">Coordinate to normalize.</param>
    /// <returns>Normalized coordinate between 0 and 1.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when value lies outside the extent.</exception>
    public double Normalize(double value)
    {
        if (value < Minimum || value > Maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Coordinate {value} falls outside the configured extent [{Minimum}, {Maximum}].");
        }

        var normalized = (value - Minimum) / Span;

        if (normalized < 0d)
        {
            return 0d;
        }

        if (normalized > 1d)
        {
            return 1d;
        }

        return normalized;
    }

    /// <summary>
    /// Clamps the provided value to the configured extent.
    /// </summary>
    public double Clamp(double value)
    {
        if (value < Minimum)
        {
            return Minimum;
        }

        if (value > Maximum)
        {
            return Maximum;
        }

        return value;
    }

    /// <summary>
    /// Gets a default extent suitable for many engineering scenarios.
    /// </summary>
    public static AxisExtent Default => new AxisExtent(-1_000_000d, 1_000_000d);
}

