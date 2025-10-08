using System;
using System.IO;
using System.Linq;
using LiteDB.Tests.Utils;
using Xunit;

namespace LiteDB.Tests.Storage;

public class LiteFileStreamTests
{
    [Fact]
    public void StreamedWritesDeferPartialChunks()
    {
        using var db = DatabaseFactory.Create();
        var fs = db.FileStorage;
        var bufferSize = LiteFileStream<string>.MAX_CHUNK_SIZE - 8192;
        var buffer = Enumerable.Range(0, bufferSize).Select(i => (byte)(i % 251)).ToArray();
        var remainder = LiteFileStream<string>.MAX_CHUNK_SIZE / 2;

        using var expected = new MemoryStream();
        using (var stream = fs.OpenWrite("file", "file.bin"))
        {
            for (var i = 0; i < 5; i++)
            {
                stream.Write(buffer, 0, buffer.Length);
                expected.Write(buffer, 0, buffer.Length);
            }

            stream.Write(buffer, 0, remainder);
            expected.Write(buffer, 0, remainder);
        }

        var expectedBytes = expected.ToArray();
        var fileInfo = fs.FindById("file");
        Assert.NotNull(fileInfo);

        var expectedLength = (long)buffer.Length * 5 + remainder;
        var expectedFullChunks = expectedLength / LiteFileStream<string>.MAX_CHUNK_SIZE;
        var expectedRemainder = (int)(expectedLength % LiteFileStream<string>.MAX_CHUNK_SIZE);
        var expectedChunks = (int)expectedFullChunks + (expectedRemainder > 0 ? 1 : 0);

        Assert.Equal(expectedLength, fileInfo.Length);
        Assert.Equal(expectedChunks, fileInfo.Chunks);

        var chunkDocs = db.GetCollection("_chunks")
            .Query()
            .Where("_id.f = @0", new BsonValue("file"))
            .OrderBy("_id.n")
            .ToList();

        Assert.Equal(expectedChunks, chunkDocs.Count);

        for (var i = 0; i < chunkDocs.Count; i++)
        {
            var length = chunkDocs[i]["data"].BinaryLength;
            if (i < chunkDocs.Count - 1)
            {
                Assert.Equal(LiteFileStream<string>.MAX_CHUNK_SIZE, length);
            }
            else
            {
                Assert.Equal(expectedRemainder == 0 ? LiteFileStream<string>.MAX_CHUNK_SIZE : expectedRemainder, length);
            }
        }

        using var readStream = fs.OpenRead("file");
        using var actual = new MemoryStream();
        readStream.CopyTo(actual);
        Assert.Equal(expectedBytes, actual.ToArray());
    }

    [Fact]
    public void SeekWithinReadStreamReturnsConsistentData()
    {
        using var db = DatabaseFactory.Create();
        var fs = db.FileStorage;
        var fullChunk = Enumerable.Range(0, LiteFileStream<string>.MAX_CHUNK_SIZE).Select(i => (byte)(i % 223)).ToArray();
        var partial = LiteFileStream<string>.MAX_CHUNK_SIZE / 3;

        using var expected = new MemoryStream();
        using (var stream = fs.OpenWrite("seek", "seek.bin"))
        {
            stream.Write(fullChunk, 0, fullChunk.Length);
            expected.Write(fullChunk, 0, fullChunk.Length);

            stream.Write(fullChunk, 0, partial);
            expected.Write(fullChunk, 0, partial);

            stream.Write(fullChunk, 0, fullChunk.Length);
            expected.Write(fullChunk, 0, fullChunk.Length);
        }

        var expectedBytes = expected.ToArray();
        var seekOffset = LiteFileStream<string>.MAX_CHUNK_SIZE + 1234;
        var readLength = 8192;
        var buffer = new byte[readLength];

        using var readStream = fs.OpenRead("seek");
        readStream.Position = seekOffset;
        var read = readStream.Read(buffer, 0, buffer.Length);
        Assert.Equal(readLength, read);
        Assert.True(expectedBytes.Skip(seekOffset).Take(readLength).SequenceEqual(buffer));
        Assert.Equal(seekOffset + readLength, readStream.Position);

        using var remainder = new MemoryStream();
        readStream.CopyTo(remainder);
        var remainingBytes = expectedBytes.Skip(seekOffset + readLength).ToArray();
        Assert.Equal(remainingBytes, remainder.ToArray());
    }
}
