using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static LiteDB.Constants;

namespace LiteDB
{
    public partial class LiteFileStream<TFileId> : Stream
    {
        private readonly Dictionary<int, long> _chunkLengths = new Dictionary<int, long>();
        private readonly BsonDocument _chunkIdFilter;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_mode != FileAccess.Read) throw new NotSupportedException();
            if (_streamPosition == Length)
            {
                return 0;
            }

            var bytesLeft = count;

            while (_currentChunkData.Length > 0 && bytesLeft > 0)
            {
                var chunkSpan = _currentChunkData.Span;
                if (_positionInChunk >= chunkSpan.Length)
                {
                    this.LoadChunkIntoState(_currentChunkIndex + 1);
                    continue;
                }

                var bytesAvailable = chunkSpan.Length - _positionInChunk;
                var bytesToCopy = Math.Min(bytesLeft, bytesAvailable);

                chunkSpan.Slice(_positionInChunk, bytesToCopy)
                    .CopyTo(new Span<byte>(buffer, offset, bytesToCopy));

                _positionInChunk += bytesToCopy;
                bytesLeft -= bytesToCopy;
                offset += bytesToCopy;
                _streamPosition += bytesToCopy;

                if (_positionInChunk >= chunkSpan.Length)
                {
                    this.LoadChunkIntoState(_currentChunkIndex + 1);
                }
            }

            return count - bytesLeft;
        }

        private ReadOnlyMemory<byte> FetchChunk(int index, out IMemoryOwner<byte> owner)
        {
            if (_chunkIdFilter == null)
            {
                owner = null;
                return ReadOnlyMemory<byte>.Empty;
            }

            _chunkIdFilter["n"] = index;

            var chunk = _chunks.FindById(_chunkIdFilter);

            if (chunk == null)
            {
                owner = null;
                return ReadOnlyMemory<byte>.Empty;
            }

            var value = chunk["data"];
            var memory = value.AsBinaryMemory;
            owner = value.DetachBinaryOwner();

            return memory;
        }

        private void LoadChunkIntoState(int index)
        {
            this.DisposeCurrentChunkOwner();

            if (index < 0)
            {
                _currentChunkData = ReadOnlyMemory<byte>.Empty;
                _currentChunkIndex = index;
                _positionInChunk = 0;
                return;
            }

            var memory = this.FetchChunk(index, out var owner);

            _currentChunkData = memory;
            _currentChunkOwner = owner;
            _currentChunkIndex = index;
            _positionInChunk = 0;

            if (!memory.IsEmpty)
            {
                _chunkLengths[index] = memory.Length;
            }
        }

        private long GetChunkLength(int index)
        {
            if (_chunkLengths.TryGetValue(index, out var length))
            {
                return length;
            }

            var memory = this.FetchChunk(index, out var owner);

            try
            {
                length = memory.Length;
                _chunkLengths[index] = length;
            }
            finally
            {
                owner?.Dispose();
            }

            return length;
        }

        private void SetReadStreamPosition(long newPosition)
        {
            if (newPosition < 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            if (newPosition >= Length)
            {
                _streamPosition = Length;
                _positionInChunk = 0;
                _currentChunkIndex = _file.Chunks;
                this.DisposeCurrentChunkOwner();
                _currentChunkData = ReadOnlyMemory<byte>.Empty;
                return;
            }

            _streamPosition = newPosition;

            long remaining = newPosition;
            int index = 0;

            while (true)
            {
                var chunkLength = this.GetChunkLength(index);

                if (chunkLength == 0)
                {
                    this.DisposeCurrentChunkOwner();
                    _currentChunkData = ReadOnlyMemory<byte>.Empty;
                    _positionInChunk = 0;
                    _currentChunkIndex = index;
                    return;
                }

                if (remaining < chunkLength)
                {
                    if (_currentChunkIndex != index || _currentChunkData.Length == 0)
                    {
                        this.LoadChunkIntoState(index);
                    }

                    _positionInChunk = (int)remaining;
                    _currentChunkIndex = index;
                    return;
                }

                remaining -= chunkLength;
                index++;
            }
        }
    }
}
