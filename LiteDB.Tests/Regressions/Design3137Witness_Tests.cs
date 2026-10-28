using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using LiteDB.Engine;
using Xunit;
using static LiteDB.Constants;

namespace LiteDB.Tests.Regressions
{
    // Research recorder, not a qualification of the deliberately refusing probe.
    public class Design3137Witness_Tests
    {
        private static string Output => Environment.GetEnvironmentVariable("DESIGN_OUT");

        [Fact]
        public void Record_header_only_same_and_foreign_journal_recovery()
        {
            var results = new List<string> { "password\tdamage\tjournal\treadonly\tattempt\tcode\tversion\tdataChanged\twalChanged\tdataLength\tlogicalLength" };
            foreach (var password in new[] { (string)null, "design-secret" })
            {
                using var own = new TempFile();
                using var foreign = new TempFile();
                using var subject = new TempFile();
                var log = FileHelper.GetLogFile(subject.Filename);
                try
                {
                    CaptureEmpty(own.Filename, password, 41, out var data, out var same);
                    CaptureEmpty(foreign.Filename, password, 99, out var other, out var different);
                    var plain = ConvertedWalFiles.Plaintext(data, password);
                    plain.Length.Should().Be(PAGE_SIZE, "a fresh database has no non-header pages");
                    new BufferSlice(plain, 0, PAGE_SIZE).ReadUInt32(HeaderPage.P_LAST_PAGE_ID).Should().Be(0);
                    if (password != null)
                    {
                        data.Skip(1).Take(16).Should().NotEqual(same.Skip(1).Take(16), "data and log AES salts are independent");
                    }
                    foreach (var damage in new[] { "intact", "crc", "zero" })
                    {
                        var input = ConvertedWalFiles.Rewrite(data, password, b =>
                        {
                            if (damage == "crc") b[BasePage.P_TRANSACTION_ID] ^= 1;
                            if (damage == "zero") Array.Clear(b, 0, PAGE_SIZE);
                        });
                        foreach (var pair in new[] { (name: "same", bytes: same), (name: "foreign", bytes: different) })
                        foreach (var readOnly in new[] { true, false })
                        {
                            File.WriteAllBytes(subject.Filename, input);
                            File.WriteAllBytes(log, pair.bytes);
                            for (var attempt = 0; attempt < 2; attempt++)
                            {
                                var version = -1;
                                var error = Record.Exception(() =>
                                {
                                    using var db = new LiteDatabase(new ConnectionString { Filename = subject.Filename, Password = password, ReadOnly = readOnly });
                                    version = db.UserVersion;
                                    db.GetCollectionNames().Should().BeEmpty();
                                });
                                var dc = !File.ReadAllBytes(subject.Filename).SequenceEqual(input);
                                var wc = !File.Exists(log) || !File.ReadAllBytes(log).SequenceEqual(pair.bytes);
                                if (error != null || readOnly) { dc.Should().BeFalse(); wc.Should().BeFalse(); }
                                results.Add(string.Join("\t", password ?? "plain", damage, pair.name, readOnly, attempt,
                                    (error as LiteException)?.ErrorCode.ToString() ?? error?.GetType().Name ?? "OK", version, dc, wc, input.Length, plain.Length));
                            }
                        }
                    }
                }
                finally
                {
                    File.Delete(log);
                    File.Delete(FileHelper.GetLogFile(own.Filename));
                    File.Delete(FileHelper.GetLogFile(foreign.Filename));
                }
            }
            File.WriteAllLines(Output + "-empty.tsv", results);
            results.Count.Should().Be(49);
        }

        [Fact]
        public void Independent_databases_can_have_identical_nonheader_pages()
        {
            using var one = new TempFile();
            using var two = new TempFile();
            var first = CreateRows(one.Filename);
            var second = CreateRows(two.Filename);
            var samePages = new List<int>();
            for (var offset = PAGE_SIZE; offset < Math.Min(first.Length, second.Length); offset += PAGE_SIZE)
                if (first.Skip(offset).Take(PAGE_SIZE).SequenceEqual(second.Skip(offset).Take(PAGE_SIZE)))
                    samePages.Add(offset / PAGE_SIZE);
            samePages.Should().Contain(3, "identical user data creates the same data page, including its CRC, in independent databases");
            first.Take(PAGE_SIZE).Should().NotEqual(second.Take(PAGE_SIZE));
            File.WriteAllText(Output + "-identical-pages.txt", "Independent modern files; identical full non-header pages: " + string.Join(",", samePages));
        }

        private static byte[] CreateRows(string filename)
        {
            using (var db = new LiteDatabase(filename))
            {
                db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = 42 });
                db.Checkpoint();
            }
            return File.ReadAllBytes(filename);
        }

        private static void CaptureEmpty(string filename, string password, int userVersion, out byte[] data, out byte[] wal)
        {
            using (var engine = new LiteEngine(new EngineSettings { Filename = filename, Password = password }))
            using (var db = new LiteDatabase(engine, disposeOnClose: false))
            {
                db.CheckpointSize = 0;
                db.UserVersion = userVersion;
                engine.SimulateDataWriteFail = page => throw new IOException("capture empty journal");
                Record.Exception(() => db.Checkpoint()).Should().BeOfType<IOException>();
            }
            data = File.ReadAllBytes(filename);
            wal = File.ReadAllBytes(FileHelper.GetLogFile(filename));
            using var raw = new MemoryStream(wal);
            using var factory = new StreamFactory(raw, password);
            using var stream = factory.GetStream(false, false);
            var journal = HeaderJournal.Read(stream);
            journal.Should().NotBeNull();
            journal.Legacy.Should().BeFalse();
            journal.ValidateCheckpointWal(stream, journal.Header);
        }
    }
}
