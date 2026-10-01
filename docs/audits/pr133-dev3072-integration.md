# PR #133: upstream #3072 and concurrency audit integration

## Scope and ancestry

Merge upstream dev `265c2497bb54b98226d2d2098986a9cdaacbc529` (#3072) and audit
branch `d3b374f2ba6b974cbbb647f53e6e9569cb24d15f` into PR #133. Both histories
remain ancestors; the upstream fix is not cherry-picked or independently rewritten.
The original audit at `98a10086c` remains historical evidence, including its failures.

## Conflict resolution

Keep upstream `SharedCallFrames` for ordinary same-file peer acquisition refusal.
Retain PR133's callback scope for handle-begin rejection, leased-reader self-disposal
validation, ownership publication during draining, native release after successful
snapshot close, and independent cleanup-error handling. A retaining reader now
enters both required scopes. Upstream frames also surround pin-holder close,
owner-exit close and the PR's `CloseOwnedCores` disposal path. No callback-needed
lock is reintroduced around draining. Both regression registries are unions; the
PR's inventory-driven repro-matrix assertions remain intact.

The explorer still requires the exact exception type and message, now matching
the upstream refusal text. No timeout or native wait is accepted as success.
Two upstream close routes originally hold a FOR UPDATE cursor on `rows` then
write `rows`. Under PR133 these are independent transactions, which correctly
refuse a self-dependent collection lock before the close callback. Hold the cursor
on `sentinel` instead and explicitly assert native ownership; all ten route/config
cases retain actual stream callbacks, refusal and complete cold-state assertions.
The first integrated run's four fixture failures are retained, not product deadlocks.

## Qualification

In progress. Final source identity, local and hosted results will be recorded here.
The original C12 merge blocker is considered resolved only after the retained
full actor matrix and upstream regressions pass on the integrated source.

Upstream #3073 remains a separate documented issue: a same-thread ordinary peer
write can wait on an idle legacy transaction that only its thread can complete.
#3072 deliberately detects executing callback ownership, not every idle owner.
This integration does not claim to fix #3073, arbitrary application wait graphs,
physical power loss or all previously documented audit gaps.
