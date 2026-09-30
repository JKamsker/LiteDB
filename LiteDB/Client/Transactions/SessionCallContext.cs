using System;
using System.Threading;

namespace LiteDB
{
    /// <summary>Cancellation of admission only; admitted storage work still drains normally.</summary>
    internal readonly struct SessionCallContext : IDisposable
    {
        [ThreadStatic] private static CancellationToken _closing;
        private readonly CancellationToken _previous;
        internal static CancellationToken Closing => _closing;
        internal SessionCallContext(CancellationToken closing)
        {
            _previous = _closing;
            _closing = closing;
        }
        public void Dispose() => _closing = _previous;
    }
}
