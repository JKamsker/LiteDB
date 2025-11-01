#nullable enable

using System;

namespace LiteDB.Spatial
{
    /// <summary>
    /// Builds <see cref="SpatialMemberOptions"/> instances using a fluent API.
    /// </summary>
    public sealed class SpatialMemberOptionsBuilder
    {
        private SpatialEngineKind? _engine;
        private BoundingBox? _domain;
        private GeographicDistanceMode? _distanceMode;
        private int? _precisionBits;
        private int? _maxCoveringCells;
        private double? _distanceTolerance;
        private string? _indexFieldName;
        private string? _boundingFieldName;

        /// <summary>
        /// Specifies the engine to use when provisioning spatial metadata.
        /// </summary>
        public SpatialMemberOptionsBuilder UseEngine(SpatialEngineKind engine)
        {
            _engine = engine == SpatialEngineKind.Automatic ? (SpatialEngineKind?)null : engine;
            return this;
        }

        /// <summary>
        /// Defines the coordinate domain associated with the dataset.
        /// </summary>
        public SpatialMemberOptionsBuilder WithDomain(BoundingBox domain)
        {
            _domain = domain;
            return this;
        }

        /// <summary>
        /// Overrides the geographic distance mode applied to the dataset.
        /// </summary>
        public SpatialMemberOptionsBuilder WithDistanceMode(GeographicDistanceMode mode)
        {
            _distanceMode = mode;
            return this;
        }

        /// <summary>
        /// Overrides the Morton encoder precision.
        /// </summary>
        public SpatialMemberOptionsBuilder WithPrecisionBits(int precisionBits)
        {
            if (precisionBits <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(precisionBits), "Precision must be a positive number of bits.");
            }

            _precisionBits = precisionBits;
            return this;
        }

        /// <summary>
        /// Overrides the maximum number of covering cells produced during planning.
        /// </summary>
        public SpatialMemberOptionsBuilder WithMaxCoveringCells(int maxCoveringCells)
        {
            if (maxCoveringCells <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCoveringCells), "The maximum number of covering cells must be positive.");
            }

            _maxCoveringCells = maxCoveringCells;
            return this;
        }

        /// <summary>
        /// Overrides the distance tolerance applied to exact filtering.
        /// </summary>
        public SpatialMemberOptionsBuilder WithDistanceTolerance(double distanceTolerance)
        {
            if (double.IsNaN(distanceTolerance) || double.IsInfinity(distanceTolerance))
            {
                throw new ArgumentOutOfRangeException(nameof(distanceTolerance), "Distance tolerance must be a finite number.");
            }

            if (distanceTolerance < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(distanceTolerance), "Distance tolerance cannot be negative.");
            }

            _distanceTolerance = distanceTolerance;
            return this;
        }

        /// <summary>
        /// Overrides the field used to store Morton index values.
        /// </summary>
        public SpatialMemberOptionsBuilder WithIndexFieldName(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                throw new ArgumentException("Index field name must be provided.", nameof(fieldName));
            }

            _indexFieldName = fieldName;
            return this;
        }

        /// <summary>
        /// Overrides the field used to store bounding boxes.
        /// </summary>
        public SpatialMemberOptionsBuilder WithBoundingBoxFieldName(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                throw new ArgumentException("Bounding box field name must be provided.", nameof(fieldName));
            }

            _boundingFieldName = fieldName;
            return this;
        }

        /// <summary>
        /// Produces an immutable <see cref="SpatialMemberOptions"/> instance.
        /// </summary>
        public SpatialMemberOptions Build()
        {
            return new SpatialMemberOptions(
                _engine,
                _domain,
                _distanceMode,
                _precisionBits,
                _maxCoveringCells,
                _distanceTolerance,
                _indexFieldName,
                _boundingFieldName);
        }
    }
}
