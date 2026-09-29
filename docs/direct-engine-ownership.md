# Direct engine ownership within a process

Ordinary file-backed Direct connections created through `LiteDatabase(string)`
or `LiteDatabase(ConnectionString)` share one live `LiteEngine` for their
canonical database path. This shares transaction locks, caches, WAL state and
native lifetime admission, rather than opening independent writable engines.
The static pool belongs to one loaded LiteDB assembly; separately loaded copies
still exclude one another through native admission.

Each database owns an independently disposable engine lease, an engine-layer
`EngineContext`, and its own mapper and client context. The storage engine does
not depend on the mapper. The engine context carries connection policy and
transaction ownership; the shared host owns storage services, caches, WAL state
and the transaction monitor.

Disposing one owner prevents further operations through that owner, including
retained collections, while other owners remain usable. Admitted operations and
returned readers retain both the host and their originating engine context.
Once those uses finish, closing the context rolls back only its unfinished
transactions. The final host release performs normal engine checkpoint/cleanup,
closes storage handles and releases admission before removing the pool entry.
Dispose readers promptly: a reader can retain exclusion even after all database
wrappers are disposed. There is no idle engine cache after the final effective
release.

Concurrent first opens publish only one engine. Closing reserves the path until
stream cleanup completes; another open cannot race ahead of buffered writes.
The registry holds weak entry references so an engine callback capturing a
database cannot create a permanent static-root cycle. A live collection retains
its engine lease even if its original database object is collected. Abandoned
lease finalizers queue context cleanup and drop references without performing
storage I/O or waiting for transactions. If another owner keeps the host alive,
its next operation drains queued contexts and rolls back abandoned transactions
without open cursors. If the entire host becomes unreachable, existing
storage/admission finalizers release native ownership; opening before they finish
fails closed. Explicit disposal is the deterministic lifetime contract. Arbitrary
abandoned partially consumed cursors retain their existing cleanup limitations;
queued cleanup does not dispose their borrowed buffers or promise to reclaim
their transactions while another owner remains live.

## Settings and transactions

Settings have explicit ownership:

| Ownership | Settings and attachment behavior |
| --- | --- |
| Connection context | `ReadOnly`, `TransactionPageLimit`, `ReadTransform`, `RejectInvalidLocalTime` and its effective timezone can differ between owners. Lazy readers retain their originating context across interleaved reads and nested callbacks. Mappers are also independent. |
| Host/storage | Password, `DurableCommits`, admission fallback policy, `CompactStorage`, `LegacyIndexScan`, effective cache size and `IndexMigrationLimitSize` must agree. Incompatible attachment fails explicitly. |
| Opening actions | `Upgrade` and `AutoRebuild` must match the live host's settings; attachment does not rerun these actions. `InitialSize` only affects initial creation. An explicitly requested collation must match the host; omission accepts its collation. |

Password checks use the live host settings, including a password changed by
rebuild; a different password cannot inherit an authenticated host. Commit
durability and compact write format remain host policies in this implementation.

A read-only context can attach to a writable host. Its operations enforce
read-only access, including writes through SQL, index creation, pragma mutation
and rebuild. Its checkpoint call performs no checkpoint; an existing-index check
does not trigger the writable host's automatic checkpoint. Other writable owners
can still modify the database, and final host cleanup follows the host's writable
policy. If the first opener created a read-only host, a later writable attachment
is rejected; close all owners before reopening writable.

Pragmas are engine state: changing timeout, checkpoint size or UTC-date behavior
through a writable owner affects every owner. Explicit transactions belong to
the pair of engine context and actual managed `Thread`. A transaction started
through A does not include B's work, even on the same thread. B's `Commit` or
`Rollback` cannot complete A's transaction, and B can start its own transaction.
Nested `BeginTrans` joins only that context's transaction on the same thread.

Collection write locks belong to transactions. Two contexts on the same thread
cannot write the same collection while either holds its write lock: the second
transaction fails immediately instead of recursively entering the first one's
lock or waiting for itself. Transactions on other threads wait subject to the
timeout. Disposing B releases only B's transactions after its active uses finish.
Keep each explicit transaction synchronous on one thread; sharing a context
between logical tasks still does not make explicit transactions async-safe.
See [explicit transactions](explicit-transactions.md).

Rebuild uses the same engine and native replacement protocol. Existing local
owners follow its reopened services; new attachment is refused during rebuild.
Exclusive operations coordinate with transactions across all contexts. A fatal
engine or context-cleanup failure stops the shared host for every owner. A stopped
engine cannot be resurrected by attaching another owner: dispose all remaining
owners and reopen. Failures preserve the existing recovery contract.

Caller-supplied engines/streams, `:memory:` and `:temp:` keep their explicit
ownership behavior. Constructing raw `LiteEngine` objects is not a pooling API.

## Why Shared engines remain separate

SharedEngine contains connection-specific transactions, reader pins, mutex owner
generations, disposal accounting and diagnostics. Pooling the complete object
would merge those semantics. This Direct refactor retains separate Shared
connections and the existing cross-process protocol. Native admission already
shares its underlying OS handle. Shared participants map the same per-database
coordination file; separate local mapping objects do not constitute independent
coordination authorities. Warm mapped reads already avoid writer-mutex
acquisition and engine reopening (`SharedCoordinatedReads_Tests` and
`SharedReadPath_Tests`).

A future process resource coordinator could share duplicate mutex handles,
mapped pages, reader-slot resources and cached file handles, principally benefiting
many overlapping or short-lived local connections. It would not eliminate the
cross-process writer mutex, durable flushes or state refresh after another process
writes. A persistent writable engine requires a different ownership protocol,
such as the experimental owner/IPC Coordinated mode. Benchmark constructor churn,
cold/warm reads and real cross-process contention before claiming a benefit.

## Validation scope

`NativeAdmissionDirectPool*_Tests` checks actual shared engine identity, concurrent
opening/writing, independent disposal, settings/authentication, same/foreign-thread
transactions, password-changing replacement, stopped-engine rejection, final
close/open ordering, reentrant disposal and weak-root collection. Cross-process
tests probe actual kernel locks with two owners and an escaping reader, then prove
admission after final release. Native and host-local backends are covered. A
killed pooled owner is followed by two cold integrity checks. Assertions preserve
committed rows, indexes, aborted-write absence and unrelated collections.

`NativeAdmissionDirectContext_Tests` targets read-only policy enforcement without
stopping writable siblings, independent transaction page limits and invalid-time
policy, context-preserving lazy transforms, and disposal from another thread.
`NativeAdmissionDirectContextLifetime_Tests` targets rebuild/checkpoint interaction
with another context's transaction, cleanup of abandoned explicit transactions
without cursors, concurrent draining of queued contexts, and host-wide failure
after an injected rollback write error. File-backed cases check committed rows,
indexes, uncommitted-write absence and unrelated data after repeated cold opens.

These are coverage descriptions, not a report that the current revision passed.
Validation results must identify the tested revision, runtime and configuration;
earlier pooling-only results do not establish the context refactor's safety.
The fault model includes process death and injected I/O errors, not every
finalizer schedule, arbitrary abandoned cursor cleanup or hardware power loss.
