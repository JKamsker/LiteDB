using System.Threading;

namespace LiteDB.Engine
{
    internal sealed class TransactionOwner
    {
        internal readonly Thread Thread = Thread.CurrentThread;
        internal readonly EngineContext Context;
        internal readonly EngineContext.TransactionSlot Slot;

        internal TransactionOwner(EngineContext context)
        {
            Context = context;
            Slot = context.Slot;
        }
    }
}
