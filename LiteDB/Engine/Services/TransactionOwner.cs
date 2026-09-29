using System.Threading;

namespace LiteDB.Engine
{
    internal sealed class TransactionOwner
    {
        internal readonly Thread Thread;
        internal readonly TransactionContext Explicit;
        internal object Admission => (object)Explicit ?? Thread;
        internal readonly EngineContext Context;
        internal readonly EngineContext.TransactionSlot Slot;

        internal TransactionOwner(EngineContext context)
        {
            Context = context;
            Explicit = TransactionContext.For(context);
            Thread = Explicit == null ? Thread.CurrentThread : null;
            Slot = context.Slot;
        }
    }
}
