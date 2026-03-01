#nullable enable

using System;

namespace LiteDB.Spatial
{
    /// <summary>
    /// Captures spatial configuration metadata associated with a mapped member.
    /// </summary>
    public sealed class SpatialMemberOptions
    {
        internal SpatialMemberOptions(
            SpatialEngineKind? engine,
            BoundingBox? domain,
            GeographicDistanceMode? distanceMode,
            int? precisionBits,
            int? maxCoveringCells,
            double? distanceTolerance,
            string? indexFieldName,
            string? boundingBoxFieldName)
        {
            Engine = engine;
            Domain = domain;
            DistanceMode = distanceMode;
            PrecisionBits = precisionBits;
            MaxCoveringCells = maxCoveringCells;
            DistanceTolerance = distanceTolerance;
            IndexFieldName = indexFieldName;
            BoundingBoxFieldName = boundingBoxFieldName;
        }

        /// <summary>
        /// Gets the preferred engine kind if explicitly configured.
        /// </summary>
        public SpatialEngineKind? Engine { get; }

        /// <summary>
        /// Gets the spatial domain associated with the member.
        /// </summary>
        public BoundingBox? Domain { get; }

        /// <summary>
        /// Gets the geographic distance mode associated with the member (when applicable).
        /// </summary>
        public GeographicDistanceMode? DistanceMode { get; }

        /// <summary>
        /// Gets the requested Morton precision in bits.
        /// </summary>
        public int? PrecisionBits { get; }

        /// <summary>
        /// Gets the requested maximum number of covering cells.
        /// </summary>
        public int? MaxCoveringCells { get; }

        /// <summary>
        /// Gets the custom distance tolerance applied during planning.
        /// </summary>
        public double? DistanceTolerance { get; }

        /// <summary>
        /// Gets the custom index field name applied to documents.
        /// </summary>
        public string? IndexFieldName { get; }

        /// <summary>
        /// Gets the custom bounding box field name applied to documents.
        /// </summary>
        public string? BoundingBoxFieldName { get; }

        internal SpatialIndexOptions ApplyTo(SpatialIndexOptions baseline)
        {
            if (baseline == null)
            {
                throw new ArgumentNullException(nameof(baseline));
            }

            var indexField = string.IsNullOrWhiteSpace(IndexFieldName) ? null : IndexFieldName;
            var boundingField = string.IsNullOrWhiteSpace(BoundingBoxFieldName) ? null : BoundingBoxFieldName;

            return baseline.With(
                precisionBits: PrecisionBits,
                maxCoveringCells: MaxCoveringCells,
                distanceTolerance: DistanceTolerance,
                indexFieldName: indexField,
                boundingBoxFieldName: boundingField);
        }
    }
}
