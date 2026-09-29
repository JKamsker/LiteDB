using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDB.Client.Shared;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class NativeAdmissionFallback_Tests
    {
        internal sealed class UnqualifiedVolume : IDisposable
        {
            private readonly Func<string, bool> _previous = DatabaseFileIdentity.UnsupportedVolume;
            internal UnqualifiedVolume(string filename)
            {
                var prefix = Path.GetFileNameWithoutExtension(filename);
                DatabaseFileIdentity.UnsupportedVolume = path => Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal);
            }
            public void Dispose() => DatabaseFileIdentity.UnsupportedVolume = _previous;
        }

        internal static ConnectionString Settings(string filename, bool shared = false, bool readOnly = false) => new ConnectionString
        {
            Filename = filename, Connection = shared ? ConnectionType.Shared : ConnectionType.Direct,
            ReadOnly = readOnly, AllowHostLocalAdmissionFallback = true
        };

        [Fact]
        public void Option_parses_alone_roundtrips_and_qualified_volume_stays_native()
        {
            new ConnectionString("AllowHostLocalAdmissionFallback=true").AllowHostLocalAdmissionFallback.Should().BeTrue();
            var text = Settings("example.db").ToString();
            new ConnectionString(text).AllowHostLocalAdmissionFallback.Should().BeTrue();
            new ConnectionString("example.db").AllowHostLocalAdmissionFallback.Should().BeFalse();
            using var file = new TempFile();
            using var db = new LiteDatabase(Settings(file));
            using var raw = new DatabaseFileLock(file, true, false);
            raw.HostLocal.Should().BeFalse();
            raw.Conflicts(DatabaseFileLock.Admission).Should().BeTrue();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Opt_in_changes_lock_authority_and_preserves_commits_rollback_indexes_and_stale_files(bool shared)
        {
            using var file = new TempFile();
            NativeAdmission_Tests.Seed(file);
            using var volume = new UnqualifiedVolume(file);
            Action denied = () => { using var db = new LiteDatabase(file); };
            denied.Should().Throw<DatabaseAdmissionException>();
            string authority;
            using (var db = new LiteDatabase(Settings(file, shared)))
            {
                db.GetCollection("rows").FindById(1)["value"].AsInt32.Should().Be(42);
                db.CheckpointSize = 0;
                db.GetCollection("rows").EnsureIndex("value");
                db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2, ["value"] = 84 });
                db.BeginTrans();
                db.GetCollection("rows").Delete(1);
                db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 3, ["value"] = 126 });
                db.Rollback();
                using var resolved = new DatabaseFileLock(file, true, false, true);
                resolved.HostLocal.Should().BeTrue();
                authority = resolved.AuthorityPath;
                using var raw = new DatabaseFileLock(authority, true, false);
                raw.Conflicts(DatabaseFileLock.Admission).Should().BeTrue("the host-local file must enforce admission");
                Action contender = () => { using var other = new LiteDatabase(Settings(file)); };
                contender.Should().Throw<DatabaseAdmissionException>();
            }
            File.Exists(authority).Should().BeTrue("last release never unlinks an authority");
            File.WriteAllText(authority, "stale bytes are not admission state");
            for (var i = 0; i < 2; i++)
            {
                using var cold = new LiteDatabase(Settings(file, shared));
                cold.GetCollection("rows").Count().Should().Be(2);
                cold.GetCollection("rows").Find("value = 84").Single()["_id"].AsInt32.Should().Be(2);
                cold.GetCollection("rows").FindById(1)["value"].AsInt32.Should().Be(42);
                ((object)cold.GetCollection("rows").FindById(3)).Should().BeNull();
                cold.GetCollection("untouched").FindById(1)["value"].AsInt32.Should().Be(99);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Shared_replacement_transfers_local_read_only_first_admission_and_password(bool readOnly)
        {
            using var file = new TempFile();
            NativeAdmission_Tests.Seed(file);
            using var volume = new UnqualifiedVolume(file);
            using (var first = new LiteDatabase(Settings(file, true, readOnly)))
            using (var second = new LiteDatabase(Settings(file, true)))
            {
                first.GetCollection("rows").Count().Should().Be(1);
                second.Rebuild();
                second.GetCollection("rows").Update(new BsonDocument { ["_id"] = 1, ["value"] = 43 });
                first.GetCollection("rows").FindById(1)["value"].AsInt32.Should().Be(43);
                second.Rebuild();
                first.GetCollection("untouched").FindById(1)["value"].AsInt32.Should().Be(99);
            }
            using (var db = new LiteDatabase(Settings(file, true)))
                db.Rebuild(new RebuildOptions { Password = "replacement" });
            for (var i = 0; i < 2; i++)
            {
                var settings = Settings(file);
                settings.Password = "replacement";
                using var cold = new LiteDatabase(settings);
                cold.GetCollection("rows").FindById(1)["value"].AsInt32.Should().Be(43);
                cold.GetCollection("untouched").FindById(1)["value"].AsInt32.Should().Be(99);
            }
        }

        [Fact]
        public async Task Shared_streaming_snapshot_blocks_recursive_mutation_and_can_close_on_another_thread()
        {
            using var file = new TempFile();
            using (var seed = new LiteDatabase(file))
            {
                seed.GetCollection("rows").InsertBulk(Enumerable.Range(1, 300).Select(i =>
                    new BsonDocument { ["_id"] = i, ["value"] = i * 2 }));
                seed.GetCollection("rows").EnsureIndex("value");
            }
            using var volume = new UnqualifiedVolume(file);
            using var engine = new SharedEngine(new EngineSettings { Filename = file, AllowHostLocalAdmissionFallback = true });
            using var db = new LiteDatabase(engine);
            using var reader = engine.Query("rows", Query.All());
            reader.Read().Should().BeTrue();
            Action write = () => db.GetCollection("rows").DeleteAll();
            Action checkpoint = () => db.Checkpoint();
            Action rebuild = () => db.Rebuild();
            write.Should().Throw<InvalidOperationException>().WithMessage("*streaming readers*");
            checkpoint.Should().Throw<InvalidOperationException>();
            rebuild.Should().Throw<InvalidOperationException>();
            var count = 1;
            while (reader.Read()) { reader.Current["value"].AsInt32.Should().Be(reader.Current["_id"].AsInt32 * 2); count++; }
            count.Should().Be(300);
            await Task.Run(() => reader.Dispose());
            db.GetCollection("rows").Update(new BsonDocument { ["_id"] = 1, ["value"] = 999 }).Should().BeTrue();
            db.Dispose();
            for (var i = 0; i < 2; i++)
            {
                using var cold = new LiteDatabase(Settings(file));
                cold.GetCollection("rows").Count().Should().Be(300);
                cold.GetCollection("rows").Find("value = 999").Single()["_id"].AsInt32.Should().Be(1);
            }
        }
    }
}
