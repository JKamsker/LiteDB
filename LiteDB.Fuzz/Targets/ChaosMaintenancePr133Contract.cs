namespace LiteDB.Fuzz.Targets;

/// <summary>
/// Historical adapter (tools/net-proofs/adapters/pr133-coverage): the close contract that fork PR #133
/// documents for its session close, used as chaos-maintenance's declared outcome sets on revisions that
/// carry that session close (capability: the PR's <c>LiteDB.SessionLifetime</c> type). Derived only
/// from <c>docs/transaction-handles.md</c> at the PR's base of the replay, 39f6c6b0:
/// <list type="bullet">
/// <item>S1 "Disposal moves a session from Open through Closing to Closed. It rejects new work, cancels pending
/// handle admission, settles idle owned handles, and drains executing work before releasing its engine lease."</item>
/// <item>S2 "Each disposal call waits up to 10 seconds, including cleanup time. A timeout leaves the session Closing
/// with needed resources retained; cleanup continues automatically when outstanding work finishes. A retry joins
/// cleanup and reports any deferred cleanup failure once."</item>
/// <item>S3 "Using a disposed <c>LiteDatabase</c> now throws <c>ObjectDisposedException</c> naming <c>LiteDatabase</c>,
/// instead of <c>LiteException</c> with <c>ENGINE_DISPOSED</c>. [...] This is an intentional exception-contract change;
/// it does not make concurrent use of a closing facade valid."</item>
/// <item>S4 "Already-open ordinary readers retain their existing independent lifetime; bound readers belong to their
/// handle/session."</item>
/// </list>
/// S2 does not name the timeout's type; it is the PR's own <c>TimeoutException</c> at 39f6c6b0
/// (<c>SessionLifetime.Close</c>, "Session close is still draining active work"). Only the close maintenance is
/// covered: the documents state no change for rebuild or a fatal write, so those keep the dev declarations.
/// On revisions without the PR's session close this returns null and the dev declarations apply unchanged.
/// </summary>
internal static class ChaosMaintenancePr133Contract
{
    /// <summary>S2: a disposal call that waited its 10 s for executing work.</summary>
    internal const string CloseTimedOut = "threw:System.TimeoutException";

    internal static readonly bool Applies =
        typeof(LiteDatabase).Assembly.GetType("LiteDB.SessionLifetime", false) != null;

    /// <summary>S2: a second Dispose joins the close and waits up to 10 s for it.</summary>
    internal static string[] SecondDispose => Applies
        ? new[] { ChaosMaintenanceDeclarations.Ok, CloseTimedOut } : null;

    /// <summary>The PR's declared set for a close scenario call, or null (dev's set applies: not a close scenario, not a
    /// PR revision, or a contract the PR keeps).</summary>
    internal static string[] Permitted(MaintenancePlan plan, string role, string op, bool beforePoint)
    {
        if (!Applies || plan.Maintenance != MaintenanceKind.Close) return null;
        var ok = new[] { ChaosMaintenanceDeclarations.Ok };
        // S3: a call on a closing or closed facade is refused (refusal marker on the PR's SessionLifetime.Enter).
        var refused = new[] { ChaosMaintenanceDeclarations.SharedDisposed };
        if (role == "maintenance") return op == "Dispose" ? new[] { ChaosMaintenanceDeclarations.Ok, CloseTimedOut } : null;
        // Active first, before its forced point: no close has started.
        if (beforePoint && plan.Order == MaintenanceOrder.ActiveFirst) return ok;
        // Maintenance first: the close started before the call arrived (S1 "rejects new work", S3).
        if (plan.Order == MaintenanceOrder.MaintenanceFirst) return refused;
        // Active first, an ordinary reader paused between rows: its existing lifetime is kept (S4), so dev's declaration applies.
        if (op == "Reader") return null;
        // Active first: the call paused at its forced point is executing work, which the close drains (S1); it
        // completes. An explicit transaction paused between its calls is idle: the close settles it (S1), and its
        // later calls, Rollback included, use a disposed facade (S3). A commit paused in its WAL write is executing.
        if (op is "TransactionUpsert" or "Rollback" || op == "Commit" && !plan.PausesInWal) return refused;
        return ok;
    }
}
