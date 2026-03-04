using System;
using System.IO;
using FluentAssertions;
using LiteDB.Engine;
using Xunit;
using static LiteDB.Constants;

namespace LiteDB.Tests.Engine
{
    public class SnapshotExceptionSafety_Tests
    {
        [Fact]
        public void Write_snapshot_should_release_collection_lock_when_collection_page_read_fails()
        {
            using var engine = new LiteEngine(new EngineSettings
            {
                DataStream = new MemoryStream(),
                LogStream = new MemoryStream()
            });

            engine.Pragma(Pragmas.TIMEOUT, 1);

            engine.Insert("docs", new[] { new BsonDocument { ["_id"] = 1 } }, BsonAutoId.Int32)
                .Should()
                .Be(1);

            var injected = 0;

            engine.SimulateDiskReadFail = buffer =>
            {
                if (buffer == null)
                {
                    return;
                }

                if (buffer.ShareCounter != BUFFER_WRITABLE)
                {
                    return;
                }

                if (buffer.ReadByte(BasePage.P_PAGE_TYPE) != (byte)PageType.Collection)
                {
                    return;
                }

                if (System.Threading.Interlocked.Exchange(ref injected, 1) != 0)
                {
                    return;
                }

                throw new InvalidOperationException("simulated collection page read failure");
            };

            Action act = () => engine.Insert("docs", new[] { new BsonDocument { ["_id"] = 2 } }, BsonAutoId.Int32);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*simulated collection page read failure*");

            engine.SimulateDiskReadFail = null;

            engine.Insert("docs", new[] { new BsonDocument { ["_id"] = 3 } }, BsonAutoId.Int32)
                .Should()
                .Be(1);
        }
    }
}

