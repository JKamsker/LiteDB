using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    /// <summary>
    /// Implement custom fast/in memory mapped disk access
    /// [ThreadSafe]
    /// </summary>
    internal class DiskService : IDisposable
    {
        private readonly MemoryCache _cache;
        private readonly EngineState _state;
        private readonly bool _readOnly;

        private IStreamFactory _dataFactory;
        private readonly IStreamFactory _logFactory;

        private StreamPool _dataPool;
        private readonly StreamPool _logPool;
        private readonly Lazy<Stream> _writer;

        private long _dataLength;
        private long _logLength;

        private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

        public DiskService(
            EngineSettings settings,
            EngineState state,
            int[] memorySegmentSizes)
        {
            _cache = new MemoryCache(memorySegmentSizes);
            _state = state;
            _readOnly = settings?.ReadOnly ?? false;


            // get new stream factory based on settings
            _dataFactory = settings.CreateDataFactory();
            _logFactory = settings.CreateLogFactory();

            // create stream pool
            _dataPool = new StreamPool(_dataFactory, false);
            _logPool = new StreamPool(_logFactory, true);

            // get lazy disk writer (log file) - created only when used
            _writer = _logPool.Writer;

            var isNew = _dataFactory.GetLength() == 0L;

            // create new database if not exist yet
            if (isNew)
            {
                if (_readOnly)
                {
                    throw new LiteException(
                        LiteException.DATABASE_READ_ONLY,
                        "Database is opened in read-only mode and cannot be created or initialized.");
                }

                LOG($"creating new database: '{Path.GetFileName(_dataFactory.Name)}'", "DISK");

                this.Initialize(_dataPool.Writer.Value, settings.Collation, settings.InitialSize);
            }

            // if not readonly, force open writable datafile
            if (settings.ReadOnly == false)
            {
                _ = _dataPool.Writer.Value.CanRead;
            }

            // get initial data file length
            _dataLength = _dataFactory.GetLength() - PAGE_SIZE;

            // get initial log file length (should be 1 page before)
            if (_logFactory.Exists())
            {
                _logLength = _logFactory.GetLength() - PAGE_SIZE;
            }
            else
            {
                _logLength = -PAGE_SIZE;
            }
        }

        /// <summary>
        /// Get memory cache instance
        /// </summary>
        public MemoryCache Cache => _cache;

        /// <summary>
        /// Create a new empty database (use synced mode)
        /// </summary>
        private void Initialize(Stream stream, Collation collation, long initialSize)
        {
            var buffer = new PageBuffer(new byte[PAGE_SIZE], 0, 0);
            var header = new HeaderPage(buffer, 0);

            // update collation
            header.Pragmas.Set(Pragmas.COLLATION, (collation ?? Collation.Default).ToString(), false);

            // update buffer
            header.UpdateBuffer();

            stream.Write(buffer.Array, buffer.Offset, PAGE_SIZE);

            if (initialSize > 0)
            {
                if (stream is AesStream) throw LiteException.InitialSizeCryptoNotSupported();
                if (initialSize % PAGE_SIZE != 0) throw LiteException.InvalidInitialSize();
                stream.SetLength(initialSize);
            }

            stream.FlushToDisk();
        }

        /// <summary>
        /// Get a new instance for read data/log pages. This instance are not thread-safe - must request 1 per thread (used in Transaction)
        /// </summary>
        public DiskReader GetReader()
        {
            return new DiskReader(_state, _cache, _dataPool, _logPool);
        }

        /// <summary>
        /// This method calculates the maximum number of items (documents or IndexNodes) that this database can have.
        /// The result is used to prevent infinite loops in case of problems with pointers
        /// Each page support max of 255 items. Use 10 pages offset (avoid empty disk)
        /// </summary>
        public uint MAX_ITEMS_COUNT => (uint)(((Volatile.Read(ref _dataLength) + Volatile.Read(ref _logLength)) / PAGE_SIZE) + 10) * byte.MaxValue;

        /// <summary>
        /// When a page are requested as Writable but not saved in disk, must be discard before release
        /// </summary>
        public void DiscardDirtyPages(IEnumerable<PageBuffer> pages)
        {
            // only for ROLLBACK action
            foreach (var page in pages)
            {
                // complete discard page and content
                _cache.DiscardPage(page);
            }
        }

        /// <summary>
        /// Discard pages that contains valid data and was not modified
        /// </summary>
        public void DiscardCleanPages(IEnumerable<PageBuffer> pages)
        {
            foreach (var page in pages)
            {
                // if page was not modified, try move to readable list
                if (_cache.TryMoveToReadable(page) == false)
                {
                    // if already in readable list, just discard
                    _cache.DiscardPage(page);
                }
            }
        }

        /// <summary>
        /// Request for a empty, writable non-linked page.
        /// </summary>
        public PageBuffer NewPage()
        {
            return _cache.NewPage();
        }

        /// <summary>
        /// Write all pages inside log file in a thread safe operation
        /// </summary>
        public int WriteLogDisk(IEnumerable<PageBuffer> pages)
        {
            this.EnsureWriteEnabled();

            var count = 0;
            var stream = _writer.Value;

            // do a global write lock - only 1 thread can write on disk at time
            lock(stream)
            {
                var logLength = Volatile.Read(ref _logLength);

                foreach (var page in pages)
                {
                    ENSURE(page.ShareCounter == BUFFER_WRITABLE, "to enqueue page, page must be writable");

                    // Reserve a new page position at the end of the log file.
                    // Avoid publishing _logLength until after the write succeeds.
                    var position = logLength + PAGE_SIZE;

                    // adding this page into file AS new page (at end of file)
                    // must add into cache to be sure that new readers can see this page
                    page.Position = position;

                    // should mark page origin to log because async queue works only for log file
                    // if this page came from data file, must be changed before MoveToReadable
                    page.Origin = FileOrigin.Log;

                    // mark this page as readable and get cached paged to enqueue
                    var readable = _cache.MoveToReadable(page);

                    try
                    {
                        // set log stream position to page
                        stream.Position = position;

#if DEBUG || TESTING
                        _state.SimulateDiskWriteFail?.Invoke(readable);
#endif

                        // and write to disk in a sync mode
                        stream.Write(readable.Array, readable.Offset, PAGE_SIZE);

                        count++;
                        logLength = position;
                    }
                    finally
                    {
                        // release page even on write failure (avoids leaking ShareCounter != 0 buffers)
                        if (readable.ShareCounter > 0)
                        {
                            readable.Release();
                        }
                    }
                }
                stream.Flush();

                Volatile.Write(ref _logLength, logLength);
            }

            return count;
        }

        /// <summary>
        /// Get file length based on data/log length variables (no direct on disk)
        /// </summary>
        public long GetFileLength(FileOrigin origin)
        {
            if (origin == FileOrigin.Log)
            {
                return Volatile.Read(ref _logLength) + PAGE_SIZE;
            }
            else
            {
                return Volatile.Read(ref _dataLength) + PAGE_SIZE;
            }
        }

        /// <summary>
        /// Mark a file with a single signal to next open do auto-rebuild. Used only when closing database (after close files)
        /// </summary>
        internal void MarkAsInvalidState()
        {
            if (_readOnly)
            {
                return;
            }

            FileHelper.TryExec(60, () =>
            {
                using (var stream = _dataFactory.GetStream(true, true))
                {
                    var buffer = _bufferPool.Rent(PAGE_SIZE);

                    try
                    {
                        stream.Position = 0;

                        var bytesRead = 0;

                        while (bytesRead < PAGE_SIZE)
                        {
                            var read = stream.Read(buffer, bytesRead, PAGE_SIZE - bytesRead);

                            if (read == 0)
                            {
                                return;
                            }

                            bytesRead += read;
                        }

                        buffer[HeaderPage.P_INVALID_DATAFILE_STATE] = 1;

                        stream.Position = 0;
                        stream.Write(buffer, 0, PAGE_SIZE);
                        stream.FlushToDisk();
                    }
                    finally
                    {
                        _bufferPool.Return(buffer, true);
                    }
                }
            });
        }

        #region Sync Read/Write operations

        /// <summary>
        /// Read all database pages inside file with no cache using. PageBuffers dont need to be Released
        /// </summary>
        public IEnumerable<PageBuffer> ReadFull(FileOrigin origin)
        {
            var pool = origin == FileOrigin.Log ? _logPool : _dataPool;
            var stream = pool.Rent();

            try
            {
                // get length before starts (avoid grow during loop)
                var length = this.GetFileLength(origin);

                stream.Position = 0;

                while (stream.Position < length)
                {
                    var position = stream.Position;

                    // Do not reuse buffers across yields: consumers may materialize the enumeration.
                    // Do not use BufferPool because header page can't be shared (byte[] is used inside page return).
                    var buffer = new byte[PAGE_SIZE];

                    var bytesRead = 0;

                    while (bytesRead < PAGE_SIZE)
                    {
                        var read = stream.Read(buffer, bytesRead, PAGE_SIZE - bytesRead);

                        if (read == 0)
                        {
                            throw new EndOfStreamException($"ReadFull reached end of stream at position {position} after reading {bytesRead} bytes.");
                        }

                        bytesRead += read;
                    }

                    yield return new PageBuffer(buffer, 0, 0)
                    {
                        Position = position,
                        Origin = origin,
                        ShareCounter = 0
                    };
                }
            }
            finally
            {
                pool.Return(stream);
            }
        }

        /// <summary>
        /// Write pages DIRECT in disk. This pages are not cached and are not shared - WORKS FOR DATA FILE ONLY
        /// </summary>
        public void WriteDataDisk(IEnumerable<PageBuffer> pages)
        {
            this.EnsureWriteEnabled();

            var stream = _dataPool.Writer.Value;
            var dataLength = Volatile.Read(ref _dataLength);

            foreach (var page in pages)
            {
                ENSURE(page.ShareCounter == 0, "this page can't be shared to use sync operation - do not use cached pages");

                dataLength = Math.Max(dataLength, page.Position);

                stream.Position = page.Position;

                stream.Write(page.Array, page.Offset, PAGE_SIZE);
            }

            stream.FlushToDisk();

            Volatile.Write(ref _dataLength, dataLength);
        }

        /// <summary>
        /// Set new length for file in sync mode. Queue must be empty before set length
        /// </summary>
        public void SetLength(long length, FileOrigin origin)
        {
            this.EnsureWriteEnabled();

            var writer = origin == FileOrigin.Log ? _logPool.Writer.Value : _dataPool.Writer.Value;

            if (origin == FileOrigin.Log)
            {
                lock (writer)
                {
                    writer.SetLength(length);
                    Volatile.Write(ref _logLength, length - PAGE_SIZE);
                }
            }
            else
            {
                writer.SetLength(length);
                Volatile.Write(ref _dataLength, length - PAGE_SIZE);
            }
        }

        /// <summary>
        /// Get file name (or Stream name)
        /// </summary>
        public string GetName(FileOrigin origin)
        {
            return origin == FileOrigin.Data ? _dataFactory.Name : _logFactory.Name;
        }

        #endregion

        private void EnsureWriteEnabled()
        {
            if (_readOnly)
            {
                throw LiteException.DatabaseReadOnly();
            }
        }

        public void Dispose()
        {
            // get stream length from writer - is safe because only this instance
            // can change file size
            var delete = _readOnly == false && _logFactory.Exists() && _logPool.Writer.Value.Length == 0;

            // dispose Stream pools
            _dataPool.Dispose();
            _logPool.Dispose();

            if (delete) _logFactory.Delete();

            // other disposes
            _cache.Dispose();
        }
    }
}
