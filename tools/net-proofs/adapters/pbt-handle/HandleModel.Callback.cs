using System.Collections.Generic;

namespace LiteDB.Tests.Concurrency.ParallelProperty
{
    public static partial class HandleModel
    {
        /// <summary>
        /// A bulk insert on handle h whose input enumeration first runs ordinary work on the same thread
        /// (documentation: "Ordinary database collections, including ones used in a mapper/input callback, remain
        /// ordinary operations"). The statement takes the target collection's write lock, then reads its input:
        /// the callback is an automatic transaction of this thread that runs while h executes and holds that lock,
        /// so a write it makes there conflicts with h (LOCK_TIMEOUT, like any conflicting holder), and its
        /// failure is caught by the callback. Then the document is inserted in h. Observation
        /// "cb=&lt;callback result&gt;;insert=&lt;1 or error&gt;", or a plain refusal / LOCK_TIMEOUT when the
        /// statement failed before reading its input. PERMISSIVE: effects are placed at one point (the callback's
        /// committed write and the handle's private insert are not separated).
        /// </summary>
        private static void ApplyCallback(ModelState state, PropertyCommand command, int thread, List<ModelOutcome> outcomes, bool strictTimeouts)
        {
            var handle = command.Slot;
            var insert = new PropertyCommand(HandleAccessKind.KindName, DataOperations.Insert, command.Collection, command.Key, command.Payload, handle);
            var status = State(state, handle);
            if (state.Pending(thread) != 0 || status == None || status != Active)
            {
                // Second point of a lock wait, or a refusal before executing: the callback never ran.
                ApplyOnHandle(state, handle, thread, insert, outcomes, strictTimeouts, borrowed: false);
                return;
            }
            outcomes.Add(new ModelOutcome(HandleObservations.Overlap, state));
            var abort = (System.Action<ModelState, ModelTransaction>)((s, t) => Abort(s, t, handle));
            if (!state.CanWrite(state.Transaction(OwnerBase + handle), command.Collection))
            {
                // The statement waits for the collection lock before it reads its input.
                Data(state, OwnerBase + handle, thread, insert, abort, outcomes, strictTimeouts, handle);
                return;
            }

            var locked = state.Clone();
            locked.AcquireWrite(locked.Transaction(OwnerBase + handle), command.Collection);
            var users = UsersBase + handle;
            if (strictTimeouts) locked.SetRegister(users, locked.Register(users) | (1 << thread));

            var callback = HandleAccessKind.CallbackCommand(command);
            var afterCallback = new List<ModelOutcome>();
            Data(locked, thread, thread, callback, ThreadSemantics.AbortFor(thread), afterCallback, strictTimeouts, -1);
            foreach (var cb in afterCallback)
            {
                if (!cb.Completes) continue;
                var inserted = new List<ModelOutcome>();
                Data(cb.Next, OwnerBase + handle, thread, insert, abort, inserted, strictTimeouts, handle);
                foreach (var outcome in inserted)
                {
                    if (!outcome.Completes) continue;
                    outcomes.Add(new ModelOutcome(HandleObservations.Callback(cb.Observation, InsertResult(outcome.Observation)), outcome.Next));
                }
            }
        }

        /// <summary>A bulk insert returns the number of documents; errors keep their observation.</summary>
        private static Observation InsertResult(Observation insert) => insert.Kind == OutcomeKind.Ok ? Observation.Ok(1) : insert;
    }
}
