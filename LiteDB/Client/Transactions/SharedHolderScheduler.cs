using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LiteDB
{
    /// <summary>Reusable native-holder threads; no owner or engine is retained while idle.</summary>
    internal static class SharedHolderScheduler
    {
        private static readonly object Gate = new object();
        private static readonly List<Worker> Idle = new List<Worker>();
        private const int MaximumIdle = 2;
        private const int IdleMilliseconds = 1000;
#if DEBUG || TESTING
        internal static int? IdleWaitOverride;
        internal static Action<Thread> BeforeIdleExpiry;
        internal static bool IsIdle(Thread thread)
        { lock (Gate) return Idle.Exists(worker => worker.Thread == thread); }
#endif

        internal static Thread Queue(Action action)
        {
            var work = new Work(action, ExecutionContext.Capture());
            try
            {
                lock (Gate)
                {
                    if (Idle.Count != 0)
                    {
                        var worker = Idle[Idle.Count - 1];
                        Idle.RemoveAt(Idle.Count - 1);
                        worker.Pending = work;
                        Monitor.PulseAll(Gate);
                        return worker.Thread;
                    }
                    // Busy workers never limit another database's writer progress.
                    var created = new Worker(work);
                    if (ExecutionContext.IsFlowSuppressed()) created.Thread.Start();
                    else using (ExecutionContext.SuppressFlow()) created.Thread.Start();
                    return created.Thread;
                }
            }
            catch { work.Context?.Dispose(); throw; }
        }

        private sealed class Work
        {
            internal readonly Action Action;
            internal readonly ExecutionContext Context;
            internal Work(Action action, ExecutionContext context) { Action = action; Context = context; }
        }

        private sealed class Worker
        {
            internal readonly Thread Thread;
            internal Work Pending;
            private ExecutionContext _cleanContext;

            internal Worker(Work work)
            {
                Pending = work;
                Thread = new Thread(Run) { IsBackground = true, Name = "LiteDB transaction mutex" };
            }

            private void Run()
            {
                // Thread startup suppressed caller flow. Even suppressed-flow jobs run
                // inside a fresh copy so their AsyncLocal/culture changes cannot leak.
                _cleanContext = ExecutionContext.Capture();
                try
                {
                    while (true)
                    {
                        ExecuteOne();
                        lock (Gate)
                        {
                            if (Idle.Count >= MaximumIdle) return;
                            Idle.Add(this);
                            var idleSince = Stopwatch.GetTimestamp();
                            var timeout = IdleMilliseconds;
#if DEBUG || TESTING
                            timeout = IdleWaitOverride ?? timeout;
#endif
                            while (Pending == null)
                            {
                                var remaining = timeout - (Stopwatch.GetTimestamp() - idleSince) * 1000d / Stopwatch.Frequency;
                                if (remaining <= 0)
                                {
#if DEBUG || TESTING
                                    BeforeIdleExpiry?.Invoke(Thread);
#endif
                                    Idle.Remove(this);
                                    return;
                                }
                                Monitor.Wait(Gate, TimeSpan.FromMilliseconds(remaining));
                            }
                        }
                    }
                }
                finally { _cleanContext?.Dispose(); }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private void ExecuteOne()
            {
                Work work;
                lock (Gate) { work = Pending; Pending = null; }
                // This entire frame must return before idle publication. Neither the
                // worker fields nor a JIT temporary may retain a completed session.
                using (var context = work.Context ?? _cleanContext.CreateCopy())
                    ExecutionContext.Run(context, state => ((Action)state)(), work.Action);
            }
        }
    }
}
