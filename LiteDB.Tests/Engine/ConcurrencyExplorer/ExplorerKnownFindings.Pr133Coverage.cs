using System.Collections.Generic;

namespace LiteDB.ConcurrencyTesting
{
    /// <summary>
    /// Coverage correction for net proofs on old revisions (tools/net-proofs/adapters/pr133-coverage): a
    /// known finding of the BASE revision, registered only on revisions that lack its fix. It is not a
    /// finding of dev (dev has the fix), so on dev and on every revision with the fix the list is empty
    /// and nothing is excluded or matched.
    /// </summary>
    internal static partial class ExplorerKnownFindings
    {
        internal const string BasePeerCallWaitsForOwnOwnership = "base-shared-peer-call-waits-for-own-outer-ownership";

        /// <summary>
        /// Capability probe: true when this revision lacks the #3072 refusal of a Shared peer call that would wait for its
        /// own outer ownership (dev 265c2497, issue #3071). The refusal lives in <c>LiteDB.Client.Shared.SharedCallFrames</c>,
        /// which arrives with #3072 and exists on every later dev revision.
        /// </summary>
        internal static bool BaseLacksPeerRefusal =>
            typeof(LiteDatabase).Assembly.GetType("LiteDB.Client.Shared.SharedCallFrames", false) == null;

        /// <summary>Scenarios whose callback dimension runs the peer body inside the actor's own Shared call or read.</summary>
        private static readonly HashSet<string> PeerCallbackScenarios = new HashSet<string>
        {
            "callback-pause", "transaction-contention", "reader-handoff"
        };

        private static IEnumerable<ExplorerKnownFinding> BaseRevisionFindings()
        {
            if (!BaseLacksPeerRefusal) yield break;
            yield return new ExplorerKnownFinding
            {
                Id = BasePeerCallWaitsForOwnOwnership,
                Summary = "Base revision without #3072 (issue #3071): a synchronous user callback (lazy input, ReadTransform, " +
                    "custom stream) runs inside a Shared call or retaining reader read that keeps the database's native mutex; " +
                    "when it calls another connection to the same database on that thread, the peer waits for the mutex forever " +
                    "(the outer ownership ends only after the callback returns). Dev refuses that wait since 265c2497 " +
                    "(refusal:shared-peer-waits-for-own-ownership). Registered only while the revision lacks the refusal.",
                Fingerprint = @"^DEADLINE_\w+@(callback-pause|transaction-contention|reader-handoff|lifetime-chaos)@mode=shared@access=\w+@maintenance=\w+@callback=peer$",
                Excludes = vector => vector.Configuration.Shared && vector.Configuration.Callback == ExplorerCallback.Peer &&
                    PeerCallbackScenarios.Contains(vector.Scenario),
                Evidence = "class 1 and 2 at PR #133 replay trees before c6e6c848f (V-A first pass): transaction-interleavings seeds " +
                    "303/404/505/707/1111/1212 and lifetime-chaos seeds 101/909 stop on DEADLINE_* with a thread blocked in " +
                    "SharedMutexOwner.Enter under an explorer callback frame under an outer SharedEngine call (classifier " +
                    "/tmp/safety-net/artifacts/v-a2/classify.py), identically at K' and F' of every row"
            };
        }
    }
}
