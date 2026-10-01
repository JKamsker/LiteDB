#if NET8_0_OR_GREATER
using System;
using System.IO;
using System.Runtime.InteropServices;
using LiteDB.Client.Shared;
using LiteDB.Engine;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class NativeAdmissionDirectCase_Tests
    {
        [Fact]
        public void Distinct_case_sensitive_files_keep_independent_engines_and_committed_indexes()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
            var directory = NewDirectory();
            try
            {
                SetCaseSensitivity(directory, true);
                var upper = Path.Combine(directory, "Data.db");
                var lower = Path.Combine(directory, "data.db");
                Seed(upper, "upper");
                Seed(lower, "lower");
                using (var upperHandle = DatabaseFileIdentity.Open(upper, true, false))
                using (var lowerHandle = DatabaseFileIdentity.Open(lower, true, false))
                    Assert.NotEqual(DatabaseFileIdentity.Read(upperHandle), DatabaseFileIdentity.Read(lowerHandle));

                using (var first = new LiteDatabase(upper))
                using (var second = new LiteDatabase(lower))
                {
                    Assert.NotSame(NativeAdmissionDirectPool_Tests.Engine(first), NativeAdmissionDirectPool_Tests.Engine(second));
                    Verify(first, "upper", 1);
                    Verify(second, "lower", 1);
                    Insert(first, "upper", 2);
                    Insert(second, "lower", 2);
                    first.Dispose();
                    Insert(second, "lower", 3);
                    Verify(second, "lower", 3);
                }

                for (var attempt = 0; attempt < 2; attempt++)
                {
                    using var first = new LiteDatabase(upper);
                    using var second = new LiteDatabase(lower);
                    Verify(first, "upper", 2);
                    Verify(second, "lower", 3);
                }
            }
            finally { Directory.Delete(directory, true); }
        }

        [Fact]
        public void Windows_case_insensitive_aliases_use_stored_spelling_and_share_the_engine()
        {
            if (!DatabaseFileIdentity.Windows) return;
            var directory = NewDirectory();
            try
            {
                SetCaseSensitivity(directory, false);
                var actual = Path.Combine(directory, "StoredCase.db");
                var alias = Path.Combine(directory, "storedcase.DB");
                Seed(actual, "same");
                var canonical = DatabaseFileIdentity.CanonicalPath(actual);
                Assert.Equal("StoredCase.db", Path.GetFileName(canonical));
                Assert.Equal(canonical, DatabaseFileIdentity.CanonicalPath(alias));
                using (var first = new LiteDatabase(actual))
                using (var second = new LiteDatabase(alias))
                {
                    Assert.Same(NativeAdmissionDirectPool_Tests.Engine(first), NativeAdmissionDirectPool_Tests.Engine(second));
                    Insert(first, "same", 2);
                    Verify(second, "same", 2);
                    first.Dispose();
                    Insert(second, "same", 3);
                }
                using var cold = new LiteDatabase(alias);
                Verify(cold, "same", 3);
            }
            finally { Directory.Delete(directory, true); }
        }

        [Fact]
        public void Case_only_file_symlink_cannot_abandon_a_separate_committed_WAL()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
            var directory = NewDirectory();
            try
            {
                SetCaseSensitivity(directory, true);
                var alias = Path.Combine(directory, "Data.db");
                var target = Path.Combine(directory, "data.db");
                using (var db = new LiteDatabase(alias))
                {
                    db.CheckpointSize = 0;
                    db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = "committed" });
                }
                var aliasLog = FileHelper.GetLogFile(alias);
                var committed = File.ReadAllBytes(aliasLog);
                File.Move(alias, target);
                // Windows CI must supply symlink privileges; setup failures must not
                // silently omit the case-sensitive WAL safety regression.
                File.CreateSymbolicLink(alias, target);
                var original = File.ReadAllBytes(target);
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var error = Assert.Throws<DatabaseAdmissionException>(() => { using var unexpected = new LiteDatabase(alias); });
                    Assert.Contains("separate WAL", error.Message);
                    Assert.Equal(committed, File.ReadAllBytes(aliasLog));
                    Assert.Equal(original, File.ReadAllBytes(target));
                    Assert.False(File.Exists(FileHelper.GetLogFile(target)));
                }
                File.Copy(aliasLog, FileHelper.GetLogFile(target));
                File.Delete(aliasLog);
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    using var recovered = new LiteDatabase(alias);
                    Assert.Equal("committed", recovered.GetCollection("rows").FindById(1)["value"].AsString);
                }
            }
            finally { Directory.Delete(directory, true); }
        }

        private static string NewDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "litedb-direct-case-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void Seed(string filename, string label)
        {
            using var db = new LiteDatabase(filename);
            db.GetCollection("rows").EnsureIndex("value", true);
            Insert(db, label, 1);
            db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = label });
        }

        private static void Insert(LiteDatabase db, string label, int id) =>
            db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = id, ["value"] = label + "-" + id });

        private static void Verify(LiteDatabase db, string label, int count)
        {
            var rows = db.GetCollection("rows");
            Assert.Equal(count, rows.Count());
            for (var id = 1; id <= count; id++)
            {
                var value = label + "-" + id;
                Assert.Equal(value, rows.FindById(id)["value"].AsString);
                Assert.Equal(id, rows.FindOne(Query.EQ("value", value))["_id"].AsInt32);
            }
            Assert.Throws<LiteException>(() => rows.Insert(new BsonDocument { ["_id"] = 99, ["value"] = label + "-1" }));
            Assert.Equal(1, db.GetCollection("sentinel").Count());
            Assert.NotNull(db.GetCollection("sentinel").FindById(label));
        }

        private static void SetCaseSensitivity(string directory, bool enabled)
        {
            if (!DatabaseFileIdentity.Windows) return;
            using var handle = CreateFileW(DatabaseFileIdentity.WindowsPath(directory), 0x100, 7,
                IntPtr.Zero, 3, 0x02000000, IntPtr.Zero); // FILE_WRITE_ATTRIBUTES, OPEN_EXISTING, BACKUP_SEMANTICS
            var openError = Marshal.GetLastWin32Error();
            Assert.True(!handle.IsInvalid, "Opening case-sensitivity test directory failed: " + openError);
            uint flags = enabled ? 1u : 0u; // FILE_CS_FLAG_CASE_SENSITIVE_DIR
            var success = SetFileInformationByHandle(handle, 23, ref flags, sizeof(uint)); // FileCaseSensitiveInfo
            var error = Marshal.GetLastWin32Error();
            Assert.True(success, "Windows tests require NTFS directory case-sensitivity support and permissions; Win32 error: " + error);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string filename, uint access, uint share,
            IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass,
            ref uint flags, int size);
    }
}
#endif
