using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace LiteDB
{
    internal sealed class BsonBinaryData : IDisposable
    {
        private IMemoryOwner<byte> _owner;
        private ReadOnlyMemory<byte> _memory;
        private byte[] _arrayCache;
        private readonly int _length;

        public BsonBinaryData(byte[] bytes)
        {
            _arrayCache = bytes ?? Array.Empty<byte>();
            _length = _arrayCache.Length;
            _memory = _arrayCache;
        }

        public BsonBinaryData(IMemoryOwner<byte> owner, int length)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

            _owner = owner;
            _memory = owner.Memory.Slice(0, length);
            _length = length;
        }

        public ReadOnlyMemory<byte> Memory => _arrayCache != null ? new ReadOnlyMemory<byte>(_arrayCache, 0, _length) : _memory;

        public ReadOnlySpan<byte> Span => _arrayCache != null ? new ReadOnlySpan<byte>(_arrayCache, 0, _length) : _memory.Span;

        public int Length => _length;

        public bool HasOwner => _owner != null;

        public byte[] GetBuffer()
        {
            if (_arrayCache != null)
            {
                if (_arrayCache.Length != _length)
                {
                    var resized = new byte[_length];
                    Array.Copy(_arrayCache, 0, resized, 0, Math.Min(_arrayCache.Length, _length));
                    _arrayCache = resized;
                }

                return _arrayCache;
            }

            var array = new byte[_length];
            _memory.Span.Slice(0, _length).CopyTo(array);
            _arrayCache = array;
            DisposeOwner();
            _memory = array;
            return array;
        }

        public IMemoryOwner<byte> DetachOwner()
        {
            var owner = _owner;
            _owner = null;
            return owner;
        }

        private void DisposeOwner()
        {
            _owner?.Dispose();
            _owner = null;
        }

        public void Dispose()
        {
            DisposeOwner();
        }
    }
}
