namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// Wraps a provider-specific geometry instance while keeping a stable test-only surface.
/// </summary>
public sealed class GeometryHandle
{
    internal GeometryHandle(object nativeGeometry)
    {
        NativeGeometry = nativeGeometry;
    }

    internal object NativeGeometry { get; }
}
