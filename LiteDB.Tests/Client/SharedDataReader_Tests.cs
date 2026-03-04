using System;
using System.Threading;
using FluentAssertions;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Client
{
    public class SharedDataReader_Tests
    {
        private sealed class ThrowOnDisposeDataReader : IBsonDataReader
        {
            public BsonValue this[string field] => throw new NotSupportedException();

            public string Collection => "test";

            public BsonValue Current => new BsonDocument();

            public bool HasValues => false;

            public bool Read() => false;

            public void Dispose()
            {
                throw new InvalidOperationException("Test reader dispose failure.");
            }
        }

        [Fact]
        public void SharedDataReader_should_invoke_close_callback_when_reader_dispose_throws()
        {
            var closeCalled = 0;
            using var shared = new SharedDataReader(new ThrowOnDisposeDataReader(), () => Interlocked.Increment(ref closeCalled));

            Action act = () => shared.Dispose();

            act.Should().Throw<InvalidOperationException>();
            closeCalled.Should().Be(1);
        }

        [Fact]
        public void SharedDataReader_should_not_invoke_close_callback_twice()
        {
            var closeCalled = 0;
            using var shared = new SharedDataReader(new ThrowOnDisposeDataReader(), () => Interlocked.Increment(ref closeCalled));

            try
            {
                shared.Dispose();
            }
            catch
            {
                // ignore
            }

            try
            {
                shared.Dispose();
            }
            catch
            {
                // ignore
            }

            closeCalled.Should().Be(1);
        }
    }
}
