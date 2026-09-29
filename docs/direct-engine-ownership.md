# Direct engine ownership within a process

Ordinary file-backed Direct connections created through `LiteDatabase(string)`
or `LiteDatabase(ConnectionString)` share one live `LiteEngine` for their
canonical database path. This shares transaction locks, caches, WAL state and
native lifetime admission, rather than opening independent writable engines.
The static pool belongs to one loaded LiteDB assembly; separately loaded copies
still exclude one another through native admission.

Each database owns an independently disposable engine lease and its own mapper
and client context. Disposing one owner prevents further operations through that
owner, including retained collections, while other owners remain usable. The
last owner closes the engine after admitted operations and returned readers have
finished. Dispose readers promptly: an outstanding reader is still a use of the
engine and retains exclusion even after all database wrappers are disposed.
There is no idle engine cache after the final effective release.

Concurrent first opens publish only one engine. Closing reserves the path until
stream cleanup completes; another open cannot race ahead of buffered writes.
The registry holds weak entry references so an engine callback capturing a
database cannot create a permanent static-root cycle. A live collection retains
its engine lease even if its original database object is collected. Abandoned
lease finalizers drop references without flushing or waiting for thread-owned
transactions; existing storage/admission finalizers release native ownership.
Opening before they finish fails closed. Explicit disposal is the deterministic
lifetime contract. Active thread-local transactions and arbitrary abandoned
partially consumed cursors retain their existing cleanup limitations.

## Settings and transactions

Owners must use compatible access and behavior settings. Password checks use the
live engine settings, including a password changed by rebuild; a different
password cannot inherit an existing authenticated engine. Read-only and writable
owners do not mix. Durability, fallback policy, compact storage, legacy scans,
upgrade/rebuild behavior, invalid-time policy, effective cache/transaction limits,
migration limits and custom read transformations must agree. An explicit collation
must match; omitted collation accepts the existing engine's collation. InitialSize
only affects creation. Mappers are independent and need not match.

Pragmas are engine state: changing timeout, checkpoint size or UTC-date behavior
through one owner affects every owner. Explicit transactions remain per engine
and per managed thread. A transaction started through A includes work through B
on that same thread, and B's nested BeginTrans returns false. Disposing B while
A remains open does not commit or roll back that transaction. Keep the entire
transaction synchronous on one thread; different logical tasks do not get
separate transactions merely by using different database wrappers.

Rebuild uses the same engine and native replacement protocol. Existing local
owners follow its reopened services; new attachment is refused during rebuild.
A stopped engine cannot be resurrected by attaching another owner: dispose all
remaining owners and reopen. Failures preserve the existing recovery contract.

Caller-supplied engines/streams, `:memory:` and `:temp:` keep their explicit
ownership behavior. Constructing raw `LiteEngine` objects is not a pooling API.

## Why Shared engines remain separate

SharedEngine contains connection-specific transactions, reader pins, mutex owner
generations, disposal accounting and diagnostics. Pooling the complete object
would merge those semantics. Native admission already shares its underlying OS
handle, and warm mapped reads already avoid writer-mutex acquisition and engine
reopening (`SharedCoordinatedReads_Tests` and `SharedReadPath_Tests`).

A future process resource coordinator could share duplicate mutex handles,
mapped pages, reader-slot resources and cached file handles, principally benefiting
many overlapping or short-lived local connections. It would not eliminate the
cross-process writer mutex, durable flushes or state refresh after another process
writes. A persistent writable engine requires a different ownership protocol,
such as the experimental owner/IPC Coordinated mode. Benchmark constructor churn,
cold/warm reads and real cross-process contention before claiming a benefit.

## Safety evidence

`NativeAdmissionDirectPool*_Tests` checks actual shared engine identity, concurrent
opening/writing, independent disposal, settings/authentication, same/foreign-thread
transactions, password-changing replacement, stopped-engine rejection, final
close/open ordering, reentrant disposal and weak-root collection. Cross-process
tests probe actual kernel locks with two owners and an escaping reader, then prove
admission after final release. Native and host-local backends are covered. A
killed pooled owner is followed by two cold integrity checks. Assertions preserve
committed rows, indexes, aborted-write absence and unrelated collections.

An independent isolated Linux .NET 10 audit passed all 39 focused pooling cases,
then deliberately broke two ownership rules. Releasing the query reference before
returning its reader failed both native/fallback cases at the child process's raw
kernel-lock assertion. Adding a strong static entry root failed the bounded
abandoned-cycle collection assertion. Restoring both changes passed 39/39 again.
These controls establish that the selected oracles detect those failures; they
do not claim all finalizer schedules or hardware power-loss behavior.
