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

The integrated code/test revision is `7f2b2bf166f7f8236f02ca1b5826fca1de656be7`.
The historical eight C12 failures all pass in the complete net8 actor matrix;
all upstream peer-callback cases pass as well: **316/316** in the combined
selection. A disjoint broader ownership selection passes **849/849** on net8.
The unchanged 300-second session limit remains in force.

The upstream production proof reproduces all six routes on published package
`6.0.0-prerelease.319` and verifies the merged source, including valid-operation
controls and cold indexed/sentinel state. Its exact binaries and fixtures are
captured in an additional explicit run because the ordinary runner retires its
build layouts. An isolated test-only mutation disabling the upstream guard fails
all ten close cases, including the adjusted fixture routes; it is an oracle test,
not a replacement for the actual known-bad package comparison.

Both local process campaigns pass 20 scenarios plus 20 recorded-input replays
per runtime on net8/net10 (80 total); Fuzz.Tests passes 36/36. net462 compilation
passes. Coverage, contract and fault registries pass; existing quarantine gaps
remain. These runs use Release with `TestingEnabled=true`, except the production
proof, which uses `TestingEnabled=false` in its own checkout.

[Hosted Fuzz 36803923340](https://github.com/JKamsker/LiteDB/actions/runs/36803923340)
passes on this integrated revision. Both Linux/net8 and Windows/net10 complete
all **272 actor schedules** and **32 process scenarios**. Unlike the historical
audit, neither actor campaign stops at the old C12 failure. The complete source
and test trees remain identical in subsequent report-only revisions.

This establishes the C12 correction in the merged code. The wider full CI matrix,
all registered regression proofs, remaining local net10 selections and exact
final candidate status are linked in [PR #133's current evidence section](https://github.com/JKamsker/LiteDB/pull/133).
Do not substitute the old `98a10086c` green CI or the historical failing audit for
that current-candidate qualification. No PR merge is performed by this integration.

Upstream #3073 remains a separate documented issue: a same-thread ordinary peer
write can wait on an idle legacy transaction that only its thread can complete.
#3072 deliberately detects executing callback ownership, not every idle owner.
This integration does not claim to fix #3073, arbitrary application wait graphs,
physical power loss or all previously documented audit gaps.
