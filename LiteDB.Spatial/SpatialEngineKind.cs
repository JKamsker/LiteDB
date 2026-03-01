namespace LiteDB.Spatial
{
    /// <summary>
    /// Represents the spatial engine families that can service plugin-managed collections.
    /// </summary>
    public enum SpatialEngineKind
    {
        /// <summary>
        /// Defer engine selection to the plugin based on the geometry type.
        /// </summary>
        Automatic = 0,

        /// <summary>
        /// Use the geographic 2D engine backed by latitude/longitude coordinates.
        /// </summary>
        Geographic2D,

        /// <summary>
        /// Use the two-dimensional Cartesian engine driven by a caller-specified domain.
        /// </summary>
        Cartesian2D,

        /// <summary>
        /// Use the three-dimensional Cartesian engine driven by a caller-specified domain.
        /// </summary>
        Cartesian3D
    }
}
