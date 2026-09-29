using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LiteDB.Client.Shared
{
    internal static class UnixAdmissionDirectory
    {
        internal static void ValidateAncestors(string root)
        {
            var darwin = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            var arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            for (var parent = Directory.GetParent(root); parent != null; parent = parent.Parent)
            {
                using var handle = DatabaseFileIdentity.Open(parent.FullName, readOnly: true, create: false);
                var stat = new byte[256];
                if (DatabaseUnixNative.Stat(handle, stat) != 0) throw DatabaseFileLock.Error("Admission ancestor ownership");
                var mode = darwin ? BitConverter.ToUInt16(stat, 4) : BitConverter.ToUInt32(stat, arm ? 16 : 24);
                var owner = BitConverter.ToUInt32(stat, darwin ? 16 : arm ? 24 : 28);
                if ((owner != 0 && owner != DatabaseUnixNative.Api.EffectiveUser()) ||
                    ((mode & 0x12) != 0 && (mode & 0x200) == 0)) // group/other write without sticky
                    throw new IOException("Host admission ancestors must prevent other users from replacing the namespace.");
                RequireNoAcl(parent.FullName);
            }
        }

        internal static void RequireNoAcl(string path)
        {
            // POSIX access ACL grants on Linux are bounded by the mode mask. macOS
            // extended ACLs are independent, so conservatively require no entries.
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
            using var handle = DatabaseFileIdentity.Open(path, readOnly: true, create: false);
            var acl = GetAcl(handle, 0x100); // ACL_TYPE_EXTENDED
            if (acl == IntPtr.Zero)
            {
                // Darwin filesec_get_property reports ENOENT when a valid fd has
                // no extended ACL property. Using an fd distinguishes absent ACL
                // from an absent pathname; every other inspection failure rejects.
                if (Marshal.GetLastWin32Error() == 2) return;
                throw DatabaseFileLock.Error("Admission directory ACL");
            }
            try
            {
                var result = GetEntry(acl, 0, out _); // ACL_FIRST_ENTRY
                // Darwin returns 0 for an entry, -1/EINVAL at end (unlike
                // POSIX implementations returning 1/0). The ACL was just opened.
                if (result == 0) throw new IOException("Host admission directories cannot have extended ACL entries.");
                if (result != -1 || Marshal.GetLastWin32Error() != 22)
                    throw DatabaseFileLock.Error("Admission ACL enumeration");
            }
            finally { FreeAcl(acl); }
        }

        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "acl_get_fd_np", SetLastError = true)]
        private static extern IntPtr GetAcl(SafeFileHandle handle, int type);
        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "acl_get_entry", SetLastError = true)]
        private static extern int GetEntry(IntPtr acl, int entry, out IntPtr result);
        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "acl_free")]
        private static extern int FreeAcl(IntPtr acl);
    }
}
