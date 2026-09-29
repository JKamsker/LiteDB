using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace LiteDB.Client.Shared
{
    internal static class WindowsAdmissionDirectory
    {
        private static readonly Lazy<string> InstallerSid = new Lazy<string>(TrustedInstaller);
        internal static void Ensure(string path)
        {
            var user = CurrentUser();
            var sddl = "O:" + user + "D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;" + user + ")";
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out var expected, out _))
                throw DatabaseFileLock.Error("Host admission security descriptor");
            try
            {
                // A parent DELETE_CHILD grant could replace even a private child.
                for (var parent = Directory.GetParent(path); parent != null; parent = parent.Parent)
                    Inspect(parent.FullName, user, IntPtr.Zero);
                var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = expected };
                if (!CreateDirectoryW(DatabaseFileIdentity.WindowsPath(path), ref attributes) && Marshal.GetLastWin32Error() != 183)
                    throw DatabaseFileLock.Error("Creating host-local admission directory");
                Inspect(path, user, expected);
            }
            finally { LocalFree(expected); }
        }

        private static void Inspect(string path, string user, IntPtr expected)
        {
            var result = GetNamedSecurityInfoW(DatabaseFileIdentity.WindowsPath(path), 1, 5,
                out var owner, out _, out var acl, out _, out var descriptor);
            if (result != 0) throw new IOException("Cannot inspect host admission directory security: " + result);
            try
            {
                if (!Trusted(Sid(owner), user) || acl == IntPtr.Zero)
                    throw new IOException("Host admission directory has an untrusted owner or unrestricted ACL: " + path + " (" + Sid(owner) + ").");
                if (expected != IntPtr.Zero)
                {
                    if (!GetSecurityDescriptorControl(descriptor, out var control, out _) || (control & 0x1000) == 0 ||
                        !GetSecurityDescriptorDacl(expected, out _, out var wanted, out _) ||
                        !Bytes(acl).SequenceEqual(Bytes(wanted)) || Sid(owner) != user)
                        throw new IOException("Existing host admission directory must have the private LiteDB ACL; it is never repaired around live owners.");
                    return;
                }
                var count = (ushort)Marshal.ReadInt16(acl, 4);
                for (var i = 0; i < count; i++)
                {
                    if (!GetAce(acl, i, out var ace)) throw DatabaseFileLock.Error("Admission ancestor ACL");
                    var type = Marshal.ReadByte(ace);
                    var flags = Marshal.ReadByte(ace, 1);
                    if ((flags & 8) != 0 || type == 1) continue; // inherit-only or deny
                    // Only plain allow ACEs have the SID at this offset. Unknown
                    // conditional/object grants cannot establish this trust proof.
                    if (type != 0) throw new IOException("Unsupported host admission ancestor ACL.");
                    var mask = (uint)Marshal.ReadInt32(ace, 4);
                    if ((mask & 0x100d0040) != 0 && !Trusted(Sid(IntPtr.Add(ace, 8)), user))
                        throw new IOException("Host admission ancestor allows an untrusted user to replace its children or ACL.");
                }
            }
            finally { LocalFree(descriptor); }
        }

        private static bool Trusted(string sid, string user) => sid == user || sid == "S-1-5-18" || sid == "S-1-5-32-544" || sid == InstallerSid.Value;
        private static string TrustedInstaller()
        {
            // Windows system-volume ancestors can be owned by this OS servicing
            // identity. Resolve its local service SID rather than trusting arbitrary
            // service accounts or an owner name supplied by the directory.
            uint size = 0, domainSize = 0;
            LookupAccountNameW(null, @"NT SERVICE\TrustedInstaller", IntPtr.Zero, ref size, null, ref domainSize, out _);
            if (Marshal.GetLastWin32Error() != 122) throw DatabaseFileLock.Error("Windows servicing identity");
            var sid = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                var domain = new StringBuilder(checked((int)domainSize));
                if (!LookupAccountNameW(null, @"NT SERVICE\TrustedInstaller", sid, ref size, domain, ref domainSize, out _))
                    throw DatabaseFileLock.Error("Windows servicing identity");
                return Sid(sid);
            }
            finally { Marshal.FreeHGlobal(sid); }
        }

        private static byte[] Bytes(IntPtr acl)
        {
            var bytes = new byte[(ushort)Marshal.ReadInt16(acl, 2)];
            Marshal.Copy(acl, bytes, 0, bytes.Length);
            return bytes;
        }
        private static string Sid(IntPtr sid)
        {
            if (!ConvertSidToStringSidW(sid, out var text)) throw DatabaseFileLock.Error("Admission SID");
            try { return Marshal.PtrToStringUni(text); }
            finally { LocalFree(text); }
        }
        private static string CurrentUser()
        {
            if (!OpenProcessToken(GetCurrentProcess(), 8, out var token)) throw DatabaseFileLock.Error("Admission process identity");
            try
            {
                GetTokenInformation(token, 1, IntPtr.Zero, 0, out var size);
                var data = Marshal.AllocHGlobal(size);
                try
                {
                    if (!GetTokenInformation(token, 1, data, size, out _)) throw DatabaseFileLock.Error("Admission token identity");
                    return Sid(Marshal.ReadIntPtr(data));
                }
                finally { Marshal.FreeHGlobal(data); }
            }
            finally { CloseHandle(token); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes
        {
            internal int Length;
            internal IntPtr Descriptor;
            internal int Inherit;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateDirectoryW(string path, ref SecurityAttributes security);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupAccountNameW(string system, string account, IntPtr sid, ref uint sidSize,
            StringBuilder domain, ref uint domainSize, out uint use);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string value, uint revision, out IntPtr descriptor, out uint size);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetNamedSecurityInfoW(string name, uint type, uint information, out IntPtr owner,
            out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSecurityDescriptorControl(IntPtr descriptor, out ushort control, out uint revision);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSecurityDescriptorDacl(IntPtr descriptor, out int present, out IntPtr acl, out int defaulted);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetAce(IntPtr acl, int index, out IntPtr ace);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ConvertSidToStringSidW(IntPtr sid, out IntPtr value);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetTokenInformation(IntPtr token, uint information, IntPtr data, int length, out int needed);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
