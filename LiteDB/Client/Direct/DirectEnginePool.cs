using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using LiteDB.Client.Shared;
using LiteDB.Engine;

namespace LiteDB.Client.Direct
{
    /// <summary>One live Direct engine per canonical file in this loaded assembly.</summary>
    internal static class DirectEnginePool
    {
        private static readonly object Gate = new object();
        // Never root an engine graph: a query callback can itself retain a database lease.
        private static readonly Dictionary<string, Slot> Entries = new Dictionary<string, Slot>(
            DatabaseFileIdentity.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        private sealed class Slot
        {
            internal WeakReference<Entry> Target;
            internal Thread Opening;
        }

        internal sealed class Entry
        {
            internal readonly object Gate = new object();
            internal readonly EngineSettings Settings;
            internal readonly LiteEngine Engine;
            internal readonly string InitialCollation;
            private readonly object _slot;
            internal int References;
            internal bool Closed, Rebuilding;

            internal Entry(EngineSettings settings, object slot)
            {
                _slot = slot;
                Settings = settings;
                Engine = new LiteEngine(settings);
                try { InitialCollation = Engine.Pragma(Pragmas.COLLATION).AsString; }
                catch { Engine.Dispose(); throw; }
            }

            internal void Retain()
            {
                lock (Gate)
                {
                    if (Closed) throw new ObjectDisposedException(nameof(LiteDatabase));
                    References++;
                }
            }

            internal void Release(bool disposing)
            {
                lock (Gate)
                {
                    if (--References != 0) return;
                    Closed = true;
                }
                try
                {
#if DEBUG || TESTING
                    if (disposing) BeforeFinalClose?.Invoke(Settings.Filename);
#endif
                    // A finalizer must not wait for thread-owned transactions or flush.
                    // Dropping the graph leaves buffered streams and critical admission
                    // to their existing ordered finalizers; early opens fail closed.
                    if (disposing) Engine.Dispose();
                }
                finally
                {
                    lock (DirectEnginePool.Gate)
                    {
                        if (Entries.TryGetValue(Settings.Filename, out var slot) && ReferenceEquals(slot, _slot))
                            Entries.Remove(Settings.Filename);
                        Monitor.PulseAll(DirectEnginePool.Gate);
                    }
                }
            }
        }

#if DEBUG || TESTING
        internal static Action<string> BeforeFinalClose;
        internal static Action<string> WaitingForClose;
        internal static bool Contains(string filename)
        {
            var canonical = DatabaseFileIdentity.CanonicalPath(filename);
            lock (Gate) return Entries.ContainsKey(canonical);
        }
#endif

        internal static ILiteEngine Open(EngineSettings settings)
        {
            if (!SharedModeGuard.IsFile(settings) || settings.LogStream != null || settings.TempStream != null)
                return new LiteEngine(settings);
            SharedModeGuard.Normalize(settings);
            var started = System.Diagnostics.Stopwatch.StartNew();
            Slot reserved;
            while (true)
            {
                lock (Gate)
                {
                    if (!Entries.TryGetValue(settings.Filename, out var slot))
                    {
                        reserved = new Slot { Opening = Thread.CurrentThread };
                        Entries.Add(settings.Filename, reserved);
                        break;
                    }
                    if (slot.Target != null && slot.Target.TryGetTarget(out var entry))
                    {
                        lock (entry.Gate)
                        {
                            if (!entry.Closed)
                            {
                                if (entry.Rebuilding) throw Conflict(settings, "The Direct engine is rebuilding; retry after replacement finishes.");
                                DirectEngineSettings.RequireCompatible(entry, settings);
                                return new DirectEngineLease(entry);
                            }
                        }
                    }
                    else if (slot.Opening == null)
                    {
                        Entries.Remove(settings.Filename);
                        continue;
                    }
                    if (ReferenceEquals(slot.Opening, Thread.CurrentThread))
                        throw Conflict(settings, "Recursive opening of the same Direct engine is unsupported.");
                    var remaining = TimeSpan.FromSeconds(5) - started.Elapsed;
#if DEBUG || TESTING
                    WaitingForClose?.Invoke(settings.Filename);
#endif
                    if (remaining <= TimeSpan.Zero || !Monitor.Wait(Gate, remaining))
                        throw Conflict(settings, "The Direct engine is opening or closing; retry later.");
                }
            }

            Entry opened = null;
            try
            {
                opened = new Entry(settings, reserved);
                var lease = new DirectEngineLease(opened);
                lock (Gate)
                {
                    reserved.Target = new WeakReference<Entry>(opened);
                    reserved.Opening = null;
                    Monitor.PulseAll(Gate);
                }
                return lease;
            }
            catch
            {
                try { opened?.Engine.Dispose(); }
                finally
                {
                    lock (Gate)
                    {
                        if (Entries.TryGetValue(settings.Filename, out var current) && ReferenceEquals(current, reserved))
                            Entries.Remove(settings.Filename);
                        Monitor.PulseAll(Gate);
                    }
                }
                throw;
            }
        }

        internal static DatabaseAdmissionException Conflict(EngineSettings settings, string message) =>
            new DatabaseAdmissionException(settings.Filename, new IOException(message));
    }
}
