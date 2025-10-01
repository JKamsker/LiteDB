namespace LiteDB.ReproRunner.Cli.Manifests;

/// <summary>
/// Identifies the operating system a repro requires.
/// </summary>
internal enum ReproOperatingSystem
{
    /// <summary>
    /// Repro can run on any supported operating system.
    /// </summary>
    Any,

    /// <summary>
    /// Repro requires a Windows environment.
    /// </summary>
    Windows,

    /// <summary>
    /// Repro requires a Linux environment.
    /// </summary>
    Linux
}
