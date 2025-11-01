#nullable enable

using System;

namespace LiteDB.Spatial
{
    /// <summary>
    /// Declares spatial indexing preferences for a mapped member.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class SpatialOptionsAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SpatialOptionsAttribute"/> class.
        /// </summary>
        public SpatialOptionsAttribute()
        {
            DistanceTolerance = double.NaN;
            Domain = Array.Empty<double>();
        }

        /// <summary>
        /// Gets or sets the preferred spatial engine for the annotated member.
        /// </summary>
        public SpatialEngineKind Engine { get; set; } = SpatialEngineKind.Automatic;

        /// <summary>
        /// Gets or sets the raw coordinate domain describing the data space.
        /// Provide four values for 2D domains or six values for 3D domains.
        /// </summary>
        public double[] Domain { get; set; }

        /// <summary>
        /// Gets or sets the precision in bits used by the Morton encoder.
        /// </summary>
        public int PrecisionBits { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of covering cells permitted during planning.
        /// </summary>
        public int MaxCoveringCells { get; set; }

        /// <summary>
        /// Gets or sets the distance tolerance applied to near queries.
        /// </summary>
        public double DistanceTolerance { get; set; }

        /// <summary>
        /// Gets or sets the document field that stores the encoded Morton index.
        /// </summary>
        public string? IndexFieldName { get; set; }

        /// <summary>
        /// Gets or sets the document field that stores bounding box values.
        /// </summary>
        public string? BoundingBoxFieldName { get; set; }

        /// <summary>
        /// Gets or sets the geographic distance mode for 2D geographic datasets.
        /// </summary>
        public GeographicDistanceMode DistanceMode { get; set; } = GeographicDistanceMode.Haversine;

        internal SpatialMemberOptions ToOptions()
        {
            var builder = new SpatialMemberOptionsBuilder();

            if (Engine != SpatialEngineKind.Automatic)
            {
                builder.UseEngine(Engine);
            }

            if (Domain != null && Domain.Length > 0)
            {
                builder.WithDomain(CreateDomain(Domain));
            }

            if (PrecisionBits > 0)
            {
                builder.WithPrecisionBits(PrecisionBits);
            }

            if (MaxCoveringCells > 0)
            {
                builder.WithMaxCoveringCells(MaxCoveringCells);
            }

            if (!double.IsNaN(DistanceTolerance))
            {
                builder.WithDistanceTolerance(DistanceTolerance);
            }

            if (!string.IsNullOrWhiteSpace(IndexFieldName))
            {
                builder.WithIndexFieldName(IndexFieldName);
            }

            if (!string.IsNullOrWhiteSpace(BoundingBoxFieldName))
            {
                builder.WithBoundingBoxFieldName(BoundingBoxFieldName);
            }

            // Always capture the distance mode; the resolver decides whether to apply it.
            builder.WithDistanceMode(DistanceMode);

            return builder.Build();
        }

        private static BoundingBox CreateDomain(double[] values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var copy = new double[values.Length];
            Array.Copy(values, copy, values.Length);

            try
            {
                return BoundingBox.Create(copy);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException("Spatial domain definitions must contain four (2D) or six (3D) finite values.", nameof(values), ex);
            }
        }
    }
}
