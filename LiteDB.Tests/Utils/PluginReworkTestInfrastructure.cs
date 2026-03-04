using System;
using System.Collections.Concurrent;
using System.Threading;
using LiteDB.Plugins;

namespace LiteDB.Tests.Utils
{
    internal sealed class TestTrackingPlugin : ILitePlugin, ILiteDatabaseHandleLifecycle
    {
        private int _initializeCount;
        private int _handleCreatedCount;
        private int _initializeConcurrent;
        private int _initializeActive;

        public int InitializeCount => Volatile.Read(ref _initializeCount);

        public int HandleCreatedCount => Volatile.Read(ref _handleCreatedCount);

        public bool InitializeWasConcurrent => Volatile.Read(ref _initializeConcurrent) != 0;

        public ConcurrentBag<int> InitializeThreadIds { get; } = new ConcurrentBag<int>();

        public ConcurrentBag<int> HandleCreatedThreadIds { get; } = new ConcurrentBag<int>();

        public ConcurrentBag<LiteDatabase> InitializedDatabases { get; } = new ConcurrentBag<LiteDatabase>();

        public ConcurrentBag<ILiteDatabase> CreatedHandles { get; } = new ConcurrentBag<ILiteDatabase>();

        public void Initialize(LiteDatabase database, ILitePluginContext context)
        {
            if (Interlocked.Increment(ref _initializeActive) > 1)
            {
                Interlocked.Exchange(ref _initializeConcurrent, 1);
            }

            try
            {
                InitializedDatabases.Add(database);
                InitializeThreadIds.Add(Environment.CurrentManagedThreadId);
                Interlocked.Increment(ref _initializeCount);
            }
            finally
            {
                Interlocked.Decrement(ref _initializeActive);
            }
        }

        public void OnHandleCreated(ILiteDatabase database)
        {
            CreatedHandles.Add(database);
            HandleCreatedThreadIds.Add(Environment.CurrentManagedThreadId);
            Interlocked.Increment(ref _handleCreatedCount);
        }
    }
}

