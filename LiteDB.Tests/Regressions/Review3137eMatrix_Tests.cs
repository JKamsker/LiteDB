using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Regressions
{
    public class Review3137eMatrix_Tests
    {
        [Fact]
        public void Primary_journal_cross_product_records_acceptance_and_preservation()
        {
            var results = new List<string> { "password\tprimary\tjournal\treadonly\tcode\tdataChanged\twalChanged\trows\tindex\terror" };
            foreach (var password in new[] { null, "wal-secret" })
            {
                using var source = new TempFile();
                using var foreign = new TempFile();
                using var subject = new TempFile();
                var sourceLog = FileHelper.GetLogFile(source.Filename);
                var foreignLog = FileHelper.GetLogFile(foreign.Filename);
                var log = FileHelper.GetLogFile(subject.Filename);
                try
                {
                    Capture(source.Filename, password, "A", 2, out var original, out var same);
                    // Set up a later fully checkpointed generation, then leave its own live journal.
                    using (var db = Open(source.Filename, password, false))
                    {
                        db.GetCollection("rows").Update(Row("A", 3));
                        db.Checkpoint();
                    }
                    CaptureNext(source.Filename, password, "A", 4, out var current, out var currentWal);
                    Capture(foreign.Filename, password, "B", 20, out var other, out var foreignWal);
                    var torn = ConvertedWalFiles.Rewrite(current, password, b => Array.Clear(b, 59, 8192 - 59));
                    var zero = ConvertedWalFiles.Rewrite(current, password, b => Array.Clear(b, 0, 8192));
                    var random = ConvertedWalFiles.Rewrite(current, password, b => { var bytes = new byte[8192]; new Random(3137).NextBytes(bytes); Buffer.BlockCopy(bytes, 0, b, 0, 8192); });
                    var primaries = new Dictionary<string, byte[]>
                    {
                        ["intact-same"] = current, ["intact-foreign"] = other,
                        ["torn-modern"] = torn, ["zero-header"] = zero, ["random-header"] = random,
                        ["released-v4-plain"] = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "../../../Resources/v4.db")),
                        ["released-v4-encrypted"] = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "../../../Resources/Issue_2494_EncryptedV4.db")),
                        ["released-5.0.21-plain"] = ConvertedWalFiles.Entry("LegacyWal_5_0_21.zip", "plain.db"),
                        ["released-5.0.21-encrypted"] = ConvertedWalFiles.Entry("LegacyWal_5_0_21.zip", "encrypted.db")
                    };
                    foreach (var v in new byte[] { 7, 9, 14 })
                        primaries["unsupported-" + v] = ConvertedWalFiles.Rewrite(ConvertedWalFiles.Entry("LegacyWal_5_0_21.zip", password == null ? "plain.db" : "encrypted.db"), password, b => b[HeaderPage.P_FILE_VERSION] = v);
                    var journals = new Dictionary<string, byte[]> { ["none"] = null, ["same"] = currentWal, ["foreign"] = foreignWal,
                        ["torn"] = ConvertedWalFiles.Rewrite(currentWal, password, b => b[b.Length - 1] ^= 1), ["stale"] = same };
                    foreach (var primary in primaries)
                    foreach (var journal in journals)
                    foreach (var readOnly in new[] { true, false })
                    {
                        File.WriteAllBytes(subject.Filename, primary.Value);
                        File.Delete(log);
                        if (journal.Value != null) File.WriteAllBytes(log, journal.Value);
                        var rows = ""; var indexed = "";
                        var error = Record.Exception(() =>
                        {
                            using var db = Open(subject.Filename, password, readOnly);
                            rows = string.Join("|", db.GetCollection("rows").FindAll().Select(x => x.ToString()));
                            indexed = string.Join("|", db.GetCollection("rows").Find(Query.EQ("_id", 1)).Select(x => x.ToString()));
                        });
                        var dc = !File.ReadAllBytes(subject.Filename).SequenceEqual(primary.Value);
                        var wc = journal.Value == null ? File.Exists(log) && new FileInfo(log).Length != 0 : !File.Exists(log) || !File.ReadAllBytes(log).SequenceEqual(journal.Value);
                        results.Add(string.Join("\t", password ?? "plain", primary.Key, journal.Key, readOnly, (error as LiteException)?.ErrorCode.ToString() ?? error?.GetType().Name ?? "OK", dc, wc, rows, indexed, error?.Message.Replace('\n', ' ').Replace('\r', ' ')));
                    }
                    // Encrypted modern primary with wrong/missing password against each journal kind.
                    if (password != null)
                    foreach (var attemptedPassword in new[] { (string)null, "wrong-password" })
                    foreach (var journal in journals)
                    foreach (var readOnly in new[] { true, false })
                    {
                        File.WriteAllBytes(subject.Filename, current);
                        File.Delete(log);
                        if (journal.Value != null) File.WriteAllBytes(log, journal.Value);
                        var error = Record.Exception(() => Open(subject.Filename, attemptedPassword, readOnly).Dispose());
                        results.Add(string.Join("\t", attemptedPassword ?? "missing", "raw-encrypted-modern", journal.Key, readOnly,
                            (error as LiteException)?.ErrorCode.ToString() ?? error?.GetType().Name ?? "OK", !File.ReadAllBytes(subject.Filename).SequenceEqual(current),
                            journal.Value == null ? File.Exists(log) && new FileInfo(log).Length != 0 : !File.Exists(log) || !File.ReadAllBytes(log).SequenceEqual(journal.Value), "", "", error?.Message.Replace('\n', ' ').Replace('\r', ' ')));
                    }
                }
                finally { File.Delete(sourceLog); File.Delete(foreignLog); File.Delete(log); }
            }
            File.WriteAllLines(Environment.GetEnvironmentVariable("REVIEW_MATRIX_OUT"), results);
            results.Count.Should().Be(261);
        }
        [Fact]
        public void Raw_encrypted_primary_missing_or_wrong_password_preserves_every_plain_journal_state()
        {
            using var encrypted = new TempFile();
            using var source = new TempFile();
            using var subject = new TempFile();
            var log = FileHelper.GetLogFile(subject.Filename);
            try
            {
                Capture(encrypted.Filename, "wal-secret", "encrypted", 2, out var primary, out _);
                Capture(source.Filename, null, "plain", 2, out _, out var stale);
                using (var db = Open(source.Filename, null, false)) db.Checkpoint();
                CaptureNext(source.Filename, null, "plain", 3, out _, out var current);
                var torn = (byte[])current.Clone(); torn[torn.Length - 1] ^= 1;
                foreach (var password in new[] { (string)null, "wrong-password" })
                foreach (var wal in new[] { null, current, stale, torn, current })
                foreach (var readOnly in new[] { true, false })
                {
                    File.WriteAllBytes(subject.Filename, primary);
                    File.Delete(log);
                    if (wal != null) File.WriteAllBytes(log, wal);
                    var error = Record.Exception(() => Open(subject.Filename, password, readOnly).Dispose());
                    error.Should().BeAssignableTo<LiteException>();
                    File.ReadAllBytes(subject.Filename).Should().Equal(primary);
                    if (wal != null) File.ReadAllBytes(log).Should().Equal(wal);
                    else (File.Exists(log) && new FileInfo(log).Length != 0).Should().BeFalse();
                }
            }
            finally
            {
                File.Delete(log);
                File.Delete(FileHelper.GetLogFile(encrypted.Filename));
                File.Delete(FileHelper.GetLogFile(source.Filename));
            }
        }
        private static BsonDocument Row(string identity, int value) => new BsonDocument { ["_id"] = 1, ["identity"] = identity, ["value"] = value };
        private static LiteDatabase Open(string filename, string password, bool readOnly) => new LiteDatabase(new ConnectionString { Filename = filename, Password = password, ReadOnly = readOnly, LegacyIndexScan = readOnly });
        private static void Capture(string filename, string password, string identity, int value, out byte[] data, out byte[] wal)
        {
            using (var db = Open(filename, password, false))
            {
                db.GetCollection("rows").Insert(Row(identity, 1));
                db.Checkpoint();
            }
            CaptureNext(filename, password, identity, value, out data, out wal);
        }
        private static void CaptureNext(string filename, string password, string identity, int value, out byte[] data, out byte[] wal)
        {
            using (var engine = new LiteEngine(new EngineSettings { Filename = filename, Password = password }))
            using (var db = new LiteDatabase(engine, disposeOnClose: false))
            {
                db.CheckpointSize = 0;
                db.GetCollection("rows").Update(Row(identity, value));
                engine.SimulateDataWriteFail = page => throw new IOException("capture");
                Record.Exception(() => db.Checkpoint()).Should().BeOfType<IOException>();
            }
            data = File.ReadAllBytes(filename);
            wal = File.ReadAllBytes(FileHelper.GetLogFile(filename));
        }
    }
}
