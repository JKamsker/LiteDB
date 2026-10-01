# PR 133: raw close blocks an active callback's fresh work

An ordinary ReadTransform callback waits for an independent collection Count on a
new application thread. The harness starts raw LiteEngine.Dispose while that
callback is active and observes its application thread reach WaitSleepJoin before
allowing the callback's dependency. No engine internals, test hooks or reflection
are used. It also observes the dependency either exit or reach a wait before the
bounded completion check, avoiding a thread-start-only timing oracle.

At eb01f346eb7d8d51925a45c2f87ef40e1c3984ee, fresh work queues behind close's
maintenance fence, while close waits for the callback. A two-second callback escape
breaks the cycle for cleanup and produces BUG_REPRODUCED (exit 0). The fixed engine
rejects fresh work immediately with ENGINE_DISPOSED, allowing the callback and close
to drain; it prints VERIFIED_FIXED (exit 10). Unexpected failures exit 1.
Both outcomes validate the rejection type and cold-reopened indexed committed row
and unrelated sentinel. This does not promise that Dispose can finish while an
arbitrary callback never returns for reasons outside the database.
