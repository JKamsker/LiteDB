# PR #133: upstream #3072 and concurrency audit integration

## Scope and ancestry

Merge upstream dev `265c2497bb54b98226d2d2098986a9cdaacbc529` (#3072) and audit
branch `d3b374f2ba6b974cbbb647f53e6e9569cb24d15f` into PR #133. Both histories
remain ancestors; the upstream fix is not cherry-picked or independently rewritten.
A subsequent fetch found upstream #3075 (`023c2b4ba8ffe637c955092ff05289d90eafdfb4`),
which was also merged: the last-reader close fixture waits for the peer's actual
release and retains a modified leased page below the writer's close threshold.
The new handle-close test follows that same deterministic setup and verifies the
updated payload after cold reopen.
The original audit at `98a10086c` remains historical evidence, including its failures.

## Conflict resolution

Keep upstream `SharedCallFrames` for ordinary same-file peer acquisition refusal.
Extend handle-begin preflight to consult those frames too. Retain PR133's
callback scope for leased-reader self-disposal
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

## Integration findings and regression guard

While qualifying the merge at `be60d53a35088e408d8265989ed915cea1983ec1`, a custom
stream callback during close could begin a peer transaction handle. Upstream's
ownership frame rejects ordinary peer calls, but the handle preflight consulted
only PR133's older public-call scopes. Five close routes, plain and encrypted,
reached a one-second admission timeout instead of immediately rejecting the
self-dependent wait. With default infinite admission the callback cannot return
to release the writer ownership. This is an integration defect; upstream has no
transaction-handle API.

The separate correction consults `SharedCallFrames` before local/native handle
admission. It includes this facade's frame because a handle acquires through a
separate child; ordinary same-facade recursion does not apply. Twenty permanent
cases cover the five cleanup routes, plain/encrypted, same-file rejection before
any admission observation and successful other-file transactions. They also
require the checkpoint callback, subsequent same-file progress, and two cold
reopens with exact indexed rows and unrelated sentinel records. The production
repro `Issue_133_SharedCloseCallbackBegin` pins the real pushed `be60d53a3`;
its before/after result is required by the regression-proof workflow.

| Cleanup boundary | Held resource | Competing callback | Required behavior | Permanent guard |
| --- | --- | --- | --- | --- |
| Retaining reader disposal | Native writer ownership and closing core | Peer handle begin | Same namespace: immediate refusal before admission; other namespace: complete | `TransactionHandleCloseFrame_Tests`, `RetainingReader` |
| Pin-holder close | Pin worker's native writer ownership | Peer handle begin | Same refusal / other-file progress | Same theory, `PinHolder` |
| Last leased reader checkpoint | Checkpoint's native writer ownership | Peer handle begin | Same refusal / other-file progress | Same theory, `LastLeasedReader` |
| Connection close with retained result | Native writer ownership and retained result/core | Peer handle begin | Same refusal / other-file progress | Same theory, `ConnectionWithEngine` |
| Connection's final checkpoint | Native writer ownership | Peer handle begin | Same refusal / other-file progress | Same theory, `ConnectionCheckpoint` |

Each row runs plain/encrypted. The cycle is cleanup → synchronous stream callback
→ child-worker native acquisition → cleanup's still-held native ownership.
Idle owners, independent leased-reader callbacks, and nested other-file operations
remain covered by the pre-existing callback control suites. This correction adds
no lock, storage retention, commit batching or new cleanup protocol.

Hosted macOS jobs 110187905983, 110187905988 and 110187906104 also exposed a
fixture namespace mismatch: caller streams bypass filename normalization while
filename-backed peers canonicalize `/var` to `/private/var`. The fixture now
canonicalizes its existing temp directory before constructing either connection;
this does not relocate databases or change the storage volume. No exception,
timeout, route or cold-state assertion was weakened. The earlier failures remain
in the evidence. The new handle-close fixture retains failing database graphs.

## Initial merge qualification (before the integration correction)

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
and test trees remained identical through report-only revision `be60d53a3`.
These results alone do not qualify the later close-frame correction.

This establishes the C12 correction in the merged code. The wider full CI matrix,
all registered regression proofs, final local net8/net10 selections and exact
final candidate status are linked in [PR #133's current evidence section](https://github.com/JKamsker/LiteDB/pull/133).
Do not substitute the old `98a10086c` green CI or the historical failing audit for
that current-candidate qualification. No PR merge is performed by this integration.

Upstream #3073 remains a separate documented issue: a same-thread ordinary peer
write can wait on an idle legacy transaction that only its thread can complete.
#3072 deliberately detects executing callback ownership, not every idle owner.
This integration does not claim to fix #3073, arbitrary application wait graphs,
physical power loss or all previously documented audit gaps.
