using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LiteDB.Client.Shared
{
    /// <summary>
    /// A fixed machine namespace, never selected from per-process temp/home paths.
    /// Files are retained indefinitely: unlinking an authority can split owners.
    /// Their contents and existence confer no admission; only OS locks do.
    /// </summary>
    internal static class HostLocalAdmission
    {
        internal static string Root => DatabaseFileIdentity.Windows
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "litedb-admission-v1")
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "/private/var/tmp/litedb-admission-v1" : "/var/tmp/litedb-admission-v1";

        internal static SafeFileHandle Open(string identity, out string path)
        {
            var root = Root;
            if (DatabaseFileIdentity.Windows) WindowsAdmissionDirectory.Ensure(root);
            else
            {
                UnixAdmissionDirectory.ValidateAncestors(root);
                if (DatabaseUnixNative.Api.MakeDirectory(root, 0x1c0) != 0 && Marshal.GetLastWin32Error() != 17)
                    throw DatabaseFileLock.Error("Creating host-local admission directory");
                // Never accept a different user's first-created namespace or a
                // directory another user can modify. Do not switch to another root.
                using var directory = DatabaseFileIdentity.Open(root, readOnly: true, create: false);
                var stat = new byte[256];
                if (DatabaseUnixNative.Stat(directory, stat) != 0) throw DatabaseFileLock.Error("Admission directory ownership");
                var darwin = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
                var arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
                var mode = darwin ? BitConverter.ToUInt16(stat, 4) : BitConverter.ToUInt32(stat, arm ? 16 : 24);
                var owner = BitConverter.ToUInt32(stat, darwin ? 16 : arm ? 24 : 28);
                if (owner != DatabaseUnixNative.Api.EffectiveUser() || (mode & 0x3f) != 0)
                    throw new IOException("The fixed host-local admission directory must be owned by this user with mode 0700.");
                UnixAdmissionDirectory.RequireNoAcl(root);
                DatabaseFileIdentity.RequireLocalVolume(directory, root);
            }
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 ||
                !string.Equals(root, DatabaseFileIdentity.CanonicalPath(root), DatabaseFileIdentity.Windows
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("Host-local admission requires a canonical directory without aliases.");
            path = System.IO.Path.Combine(root, identity + ".lock");
            // Check both before and after open; the fixed directory is trusted and
            // must not be cleaned or externally replaced while any process uses it.
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Host-local admission files cannot be links.");
            var handle = DatabaseFileIdentity.Open(path, readOnly: false, create: true);
            try
            {
                DatabaseFileIdentity.RequireLocalVolume(handle, path);
                var physical = DatabaseFileIdentity.Read(handle);
                using var check = DatabaseFileIdentity.Open(path, readOnly: true, create: false);
                if (DatabaseFileIdentity.Read(check) != physical ||
                    (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Host-local admission authority changed during open.");
                return handle;
            }
            catch { handle.Dispose(); throw; }
        }
    }
}
