using System;
using System.Buffers;
using System.IO;
using System.Linq;
using static LiteDB.Constants;

namespace LiteDB
{
    public partial class LiteFileStream<TFileId> : Stream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            _streamPosition += count;

            _buffer.Write(buffer, offset, count);

            if (_buffer.Length >= MAX_CHUNK_SIZE)
            {
                this.WriteChunks(false);
            }
        }

        public override void Flush()
        {
            // write last unsaved chunks
            this.WriteChunks(true);
        }

        /// <summary>
        /// Consume all _buffer bytes and write to chunk collection
        /// </summary>
        private void WriteChunks(bool flush)
        {
            if (_buffer.Length == 0 && flush == false)
            {
                return;
            }

            if (!_buffer.TryGetBuffer(out ArraySegment<byte> segment))
            {
                var bufferCopy = _buffer.ToArray();
                segment = new ArraySegment<byte>(bufferCopy, 0, bufferCopy.Length);
            }

            var totalLength = (int)_buffer.Length;
            var bytesToPersist = flush ? totalLength : totalLength - (totalLength % MAX_CHUNK_SIZE);
            var processed = 0;

            while (processed < bytesToPersist)
            {
                var chunkSize = Math.Min(MAX_CHUNK_SIZE, bytesToPersist - processed);

                var chunk = new BsonDocument
                {
                    ["_id"] = new BsonDocument
                    {
                        ["f"] = _fileId,
                        ["n"] = _file.Chunks++ // zero-based index
                    }
                };

                var owner = MemoryPool<byte>.Shared.Rent(chunkSize);
                BsonValue dataValue = null;
                try
                {
                    var target = owner.Memory.Span.Slice(0, chunkSize);
                    new ReadOnlySpan<byte>(segment.Array, segment.Offset + processed, chunkSize).CopyTo(target);

                    dataValue = new BsonValue(owner, chunkSize);
                    chunk["data"] = dataValue;

                    _chunks.Insert(chunk);
                }
                finally
                {
                    if (dataValue != null)
                    {
                        dataValue.DisposeBinaryOwner();
                    }
                    else
                    {
                        owner.Dispose();
                    }
                }

                processed += chunkSize;
            }

            var remaining = totalLength - processed;

            if (!flush)
            {
                if (remaining > 0)
                {
                    if (segment.Array != null)
                    {
                        Buffer.BlockCopy(segment.Array, segment.Offset + processed, segment.Array, segment.Offset, remaining);
                    }

                    _buffer.SetLength(remaining);
                    _buffer.Position = remaining;
                }
                else
                {
                    _buffer.SetLength(0);
                    _buffer.Position = 0;
                }
            }
            else
            {
                _buffer.SetLength(0);
                _buffer.Position = 0;

                _file.UploadDate = DateTime.Now;
                _file.Length = _streamPosition;

                _files.Upsert(_file);
            }
        }
    }
}
