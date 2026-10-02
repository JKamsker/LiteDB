# Storage, transactions, and ownership

Use this for WAL/rebuild, cursors, locks, FileStorage, buffers, and disposal.
Apply [data safety](data-safety.md) first: preserve consistency through failures,
stop unsafe continuation, and make detected corruption visible.

## Transaction and cursor lifetime

- Reader cursors can outlive their opening thread. Retain the actual `Thread`
  identity for admission, lookup, cleanup, and completion guards. Managed thread
  IDs can be recycled; use them only for diagnostics.
- A foreign thread disposing a query must release that query's lease without
  committing, rolling back, or disposing the caller's independent transaction.
  Guard failures must leave the owner's transaction intact.
- Distinguish a transaction created by an operation from one it joined. A failed
  `BeginTrans`/join result or an occupied shared mutex alone does not establish
  explicit-transaction ownership. Include transparent public engine decorators.
- A new blocking site (lock, gate, mutex, event, handoff or poll loop) must register
  with the [wait-for graph](../wait-for-graph.md): its wait before blocking, its holds,
  and the frames in which its owner executes; or be listed there with the reason it is not.
  The graph latches each cycle it finds before the wait blocks and reports it per test.
- Keep publication, ownership handoff, and cleanup ordered. Cleanup after releasing
  a lock must not erase the next owner's state. Check abandoned-owner paths as
  well as ordinary completion.
- Cursors retain all snapshots they use, including referenced collections through
  INCLUDE. Upgrading a collection snapshot for writing must not dispose a snapshot
  still reachable by an active cursor or replay pipeline.

Read [explicit transactions](../explicit-transactions.md),
[reader ownership](../issue-2991-reader-ownership.md), and
[regression coverage](../transaction-regression-coverage.md).

## WAL, rebuild, and FileStorage

- Treat the data file and its WAL as one recoverable state. A restore that makes
  old data live while stranding its acknowledged WAL-only commits is incomplete.
  Exercise failures in both restore orders and repeated recovery attempts.
- Do not delete backups or replacement candidates until a complete usable state
  has been established. Distinguish corruption from transient I/O failure before
  repairing or discarding pages. Preserve the original failure and recovery data.
- File-backed opens check the rebuild recovery marker before upgrade, automatic
  rebuild, or database creation. Create and flush it before installation renames;
  clear it only when a complete original data/WAL pair or completed replacement
  is confirmed live. Repeated recovery failures must block direct and shared opens,
  including when the live file is missing. Preserve the original exception and
  rollback errors; synchronize retained engine settings if the replacement remains
  live. See [rebuild recovery](../rebuild-recovery.md).
- Changes to transaction-ID allocation, checkpoint, or WAL reuse need tests for
  abandoned/unconfirmed pages, live snapshots, and crashes between publication
  steps. Reusing bytes must not resurrect an older transaction's pages.
- FileStorage metadata and chunks must remain consistent through upload, delete,
  append, metadata updates, throwing streams, rollback, and explicit transactions.
  Include incremental `OpenWrite` and active cursors. Lock-order analysis must
  include locks already held by the caller, not only the helper's local order.
- Do not hide transactional side effects inside an enumerable that assumes a
  decorator will consume it once, lazily, and only after acquiring a lock.
- For file-ownership changes, test aliases and sidecar identity, read-only sharing,
  rebuild/reopen, caller streams, platform fallbacks, and crash cleanup of scratch
  files. Lexical path normalization alone does not prove physical-file identity.
  Shared connections must bind the data/WAL/temp paths, mutex and reader registry
  to the same construction-time absolute filename across every reopen. Retain
  degraded durability diagnostics across those reopenings without suppressing
  future device-sync attempts. See [shared-mode safety](../shared-mode-safety.md).
- Validate native admission together with the runtime's file-sharing locks on
  each OS. Darwin combines OFD and `flock` locks; admitted data streams must not
  add whole-file locks that conflict with admission or obscure family probes.
  Keep admission through ordinary buffered-stream finalization and transfer it
  across replacement before publication. See [native admission](../native-database-admission.md).
  Critical-finalizer ordering covers one collection, not unrelated objects across
  GC generations. Preserve acquisition before buffered stream construction, and
  control both generation and simultaneous root release in finalizer-order tests.
  Verify native exclusion independently of registry, recovery-marker and mutex
  refusals: those mechanisms can hide a prematurely closed OS handle. Admission
  compatibility must include the storage/coordination namespace, not just inode
  and mode; aliases must not create independent WAL or writer-mutex identities.

Long-lived native-owner threads must not root the application graph whose
abandonment signals their release. Check callbacks, captured execution contexts,
disposed cancellation registrations, and fields on public settings subclasses.
Copy effective configuration into a detached snapshot; preserve serialized policy
and unset/default distinctions when adding settings. Test graph collection and
native release with a committed indexed sentinel and abandoned writes. Reusable
cleanup workers must return their callback stack frame and restore their execution
context before becoming idle, and blocked workers must not starve unrelated owners.

## Buffers and cleanup

Every pin, pooled buffer, cursor, and underlying stream needs a clear owner and
release path for completion, early termination, exceptions, and repeated disposal.
Disposal admission must be atomic; lazy resource publication must coordinate with
concurrent disposal. Logging/callback failures must not skip cleanup or replace the
original error. Respect caller-stream ownership. Mark a new or changed teardown path
`[TeardownPath]` with its declared fault disposition and bracket its failing steps; the
[teardown sweep](../teardown-sweep.md) fails until the path has a driver.

Borrowed views cannot outlive the page/buffer that backs them. `byte[]` storage
marshalled into structs may contain only unmanaged data; keep managed references
in GC-visible storage. Do not remove cache lifetime synchronization as a mechanical
performance tweak. See [memory lifecycle audit](../memory-lifecycle-audit.md).
