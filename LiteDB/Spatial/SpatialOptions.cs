using System;

namespace LiteDB.Spatial
{
    public enum DistanceFormula
    {
        Haversine,
        Vincenty
    }

    public enum AngleUnit
    {
        Degrees,
        Radians
    }

    public sealed class SpatialOptions
    {
        private double _numericToleranceDegrees = 1e-9;

        public DistanceFormula Distance { get; set; } = DistanceFormula.Haversine;

        public bool SortNearByDistance { get; set; } = true;

        public int MaxCoveringCells { get; set; } = 32;

        public AngleUnit AngleUnit { get; set; } = AngleUnit.Degrees;

        public int DefaultIndexPrecisionBits { get; set; } = 52;

        public double BoundingBoxPaddingMeters { get; set; } = 0d;

        public double DistanceToleranceMeters { get; set; } = 0.001d;

        public double NumericToleranceDegrees
        {
            get => _numericToleranceDegrees;
            set => _numericToleranceDegrees = Math.Max(0d, value);
        }

#pragma warning disable CS0618 // Maintain compatibility for callers still using IndexPrecisionBits.
        [Obsolete("Use DefaultIndexPrecisionBits instead.")]
        public int IndexPrecisionBits
        {
            get => DefaultIndexPrecisionBits;
            set => DefaultIndexPrecisionBits = value;
        }
#pragma warning restore CS0618
    }
}
