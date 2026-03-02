using System;
using System.IO;
using FluentAssertions;
using LiteDB.Engine;
using LiteDB.Tests.Utils;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class ReadOnlyOpen_Tests
    {
        [Fact]
        public void Opening_empty_stream_in_read_only_should_not_write()
        {
            var stream = new MemoryStream();
            stream.Length.Should().Be(0);

            Action act = () =>
            {
                using var db = new LiteDatabaseBuilder()
                    .UseStream(stream)
                    .AsReadOnly()
                    .Build();
            };

            act.Should().Throw<LiteException>()
                .Where(ex => ex.ErrorCode == LiteException.DATABASE_READ_ONLY);

            stream.Length.Should().Be(0);
        }

        [Fact]
        public void AutoRebuild_in_read_only_should_throw_and_not_modify_file()
        {
            using var file = new TempFile();

            using (var setup = new LiteDatabase(file.Filename))
            {
                setup.GetCollection<BsonDocument>("docs").Insert(new BsonDocument { ["_id"] = 1 });
                setup.Checkpoint();
            }

            MarkDatabaseAsInvalid(file.Filename);

            var settings = new EngineSettings
            {
                Filename = file.Filename,
                AutoRebuild = true,
                ReadOnly = true
            };

            Action act = () => new LiteEngine(settings);

            act.Should().Throw<LiteException>()
                .Where(ex => ex.ErrorCode == LiteException.DATABASE_READ_ONLY);

            ReadInvalidStateFlag(file.Filename).Should().Be(1);
        }

        [Fact]
        public void StreamFactory_should_not_return_writable_streams_when_read_only()
        {
            var baseStream = new MemoryStream();
            var factory = new StreamFactory(baseStream, password: null, readOnly: true);

            using var stream = factory.GetStream(canWrite: true, sequencial: false);

            stream.CanWrite.Should().BeFalse();

            Action act = () => stream.WriteByte(1);

            act.Should().Throw<NotSupportedException>();
        }

        private static void MarkDatabaseAsInvalid(string filename)
        {
            using var stream = new FileStream(filename, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            stream.Position = HeaderPage.P_INVALID_DATAFILE_STATE;
            stream.WriteByte(1);
            stream.Flush();
        }

        private static int ReadInvalidStateFlag(string filename)
        {
            using var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Position = HeaderPage.P_INVALID_DATAFILE_STATE;
            return stream.ReadByte();
        }
    }
}

