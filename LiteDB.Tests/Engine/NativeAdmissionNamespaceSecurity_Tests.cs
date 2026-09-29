#if NET8_0_OR_GREATER
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FluentAssertions;
using LiteDB.Client.Shared;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class NativeAdmissionNamespaceSecurity_Tests
    {
        [Fact]
        public void Unix_writable_nonsticky_ancestor_cannot_split_authority()
        {
            if (OperatingSystem.IsWindows()) return;
            var root = Path.Combine(Path.GetTempPath(), "litedb-admission-security-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupWrite | UnixFileMode.OtherWrite);
                Action inspect = () => UnixAdmissionDirectory.ValidateAncestors(Path.Combine(root, "authority"));
                inspect.Should().Throw<IOException>().WithMessage("*ancestors*");
                File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                inspect.Should().NotThrow();
            }
            finally { Directory.Delete(root); }
        }

        [Fact]
        public void Darwin_extended_acl_is_refused_and_empty_acl_is_accepted()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
            var root = Path.Combine(Path.GetTempPath(), "litedb-admission-acl-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                UnixAdmissionDirectory.RequireNoAcl(root);
                var start = new ProcessStartInfo("chmod") { UseShellExecute = false };
                foreach (var argument in new[] { "+a", "everyone allow delete_child", root }) start.ArgumentList.Add(argument);
                using (var process = Process.Start(start)) { process.WaitForExit(); process.ExitCode.Should().Be(0); }
                Action inspect = () => UnixAdmissionDirectory.RequireNoAcl(root);
                inspect.Should().Throw<IOException>().WithMessage("*extended ACL*");
            }
            finally { Directory.Delete(root); }
        }

        [Fact]
        public void Windows_ancestor_delete_grant_cannot_split_authority()
        {
            if (!OperatingSystem.IsWindows()) return;
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "litedb-admission-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                WindowsAdmissionDirectory.Ensure(root);
                var directory = new DirectoryInfo(root);
                var security = directory.GetAccessControl();
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    FileSystemRights.Delete, AccessControlType.Allow));
                directory.SetAccessControl(security);
                Action inspect = () => WindowsAdmissionDirectory.Ensure(Path.Combine(root, "authority"));
                inspect.Should().Throw<IOException>().WithMessage("*untrusted user*");
                Directory.Exists(Path.Combine(root, "authority")).Should().BeFalse();
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Windows_private_directory_reopens_but_inherited_acl_is_not_repaired()
        {
            if (!OperatingSystem.IsWindows()) return;
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "litedb-admission-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                WindowsAdmissionDirectory.Ensure(root);
                WindowsAdmissionDirectory.Ensure(root);
                var weak = Path.Combine(root, "inherited");
                Directory.CreateDirectory(weak);
                File.WriteAllText(Path.Combine(weak, "sentinel"), "preserve");
                Action inspect = () => WindowsAdmissionDirectory.Ensure(weak);
                inspect.Should().Throw<IOException>().WithMessage("*private LiteDB ACL*");
                File.ReadAllText(Path.Combine(weak, "sentinel")).Should().Be("preserve");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
#endif
