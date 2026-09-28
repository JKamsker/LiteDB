# Native admission safety evidence

This is a bounded application of [#3034](https://github.com/litedb-org/LiteDB/issues/3034)
to [#3046](https://github.com/litedb-org/LiteDB/pull/3046). The protocol and platform
contract remain in [native database admission](native-database-admission.md).

## Rules and discriminating scenarios

| Rule | Permitted outcome and execution proof | Executable evidence |
| --- | --- | --- |
| ADMISSION-1: one physical database has one active storage/coordination identity | Compatible same-path owners make progress. An alternate canonical bind path is rejected before data/WAL mutation, both locally and in another process. | `NativeAdmissionValidation`: real directory bind mounts, same-path positive controls, exact data/WAL bytes, production assembly with hooks absent; `NativeAdmissionPathProof_Tests`: four domains, both endpoints, distances 1/2^32/2^59, forced overlapping differing claims. |
| ADMISSION-2: ownership ends exactly at the final effective release | A returned reference keeps the kernel lock despite another owner's disposal; failed publication keeps no phantom reference. Ordinary buffer finalizers still observe native exclusion. | `NativeAdmissionOwnershipProof_Tests`: pause at actual reference publication while foreign-thread disposal starts; success/failure × Direct/Shared; raw descriptor probe before/after release. `NativeAdmissionFinalizer_Tests`: independent descriptor inside the ordinary finalizer and successful admission after finalization. |
| ADMISSION-3: replacement protects both generations | Before marker creation and throughout publication, both distinct inodes have native admission. Afterwards only the live inode remains held; final release unlocks it. | `NativeAdmissionHandoffProof_Tests`: four reached boundaries × Direct/Shared × plain/encrypted; parent probes real child-owned source/candidate handles, asserts inode change and byte preservation, then cold logical verification. Existing rollback/failure/crash suites remain enabled. |
| ADMISSION-4: admission changes do not lose acknowledged effects or expose aborted effects | Committed update/delete/insert/FileStorage effects survive two actual rebuilds, owner turnover and two cold processes; aborted effects remain absent; unrelated rows and unique indexes survive. | Production `NativeAdmissionValidation`: explicit independent expected rows/blob/metadata, index-plan assertion, uniqueness violation control, read-only-first Shared owner retained through replacement, fresh-process checks and a subsequent write/checkpoint/reopen. |
| ADMISSION-5: refusal is bounded by its contract | A conflict/unsupported environment does not mutate storage; after owner death or final release supported opens succeed. Incomplete replacement remains guarded with recovery evidence intact. | Existing native process, crash, aliases, permission, unsupported-volume, failed-downgrade and Shared-lifetime suites. Five real Shared processes, repeated kills, last-reference checks, repeated byte-preserving refused opens and complete recovery-candidate checks. |

These tests retain positive controls: rejecting every connection, omitting a
rebuild, returning an empty result or never releasing admission does not pass.
The handoff test checks distinct physical identities, not just callback names.
The path tests exercise 64-bit range lengths and skip-empty semantics explicitly.
The finalizer oracle roots its independent model owner and guard together until
both reach the oldest GC generation, then releases the root atomically. This
tests the runtime's same-collection ordering contract while retaining both
allocation orders and the independent kernel probe. Production acquires admission
before constructing buffered database/WAL streams. An unrelated older model owner
surviving a young-generation collection of its guard does not model that order.

`NativeAdmissionGraphFinalizer_Tests` complements that model with real Direct and
Shared engine graphs. It drains a real `IBsonDataReader` without disposing it;
the Shared reader still owns its snapshot/lease closure. The test observes actual
data and WAL stream construction and pauses an actual stream's ordinary finalizer.
Every writable finalizer probes native exclusion before and after buffered-write
cleanup; the Direct cases require this boundary to execute. Shared operation writers
close normally before the exhausted snapshot reader is abandoned. Rooted graphs and
older abandoned graphs after a forced young collection must retain native exclusion;
full collections eventually release it. Read-only streams in thread-local pools
can outlive the abandoned engine and its guard; the test additionally requires every observed stream to close within
the bounded collection loop before cold verification and fixture deletion. It does
not require admission during cleanup of those unused read-only handles.
Two cold opens check the acknowledged WAL insert, indexed lookup, rollback absence
and unrelated collection. Test-only FileStream observers add no reference back to
the engine or guard and are absent from production builds. This case excludes
mid-iteration cursor abandonment: that separate scenario can trigger the existing
test-build assertion for a finalized pinned `PageBuffer`; it is not silently
suppressed or claimed as covered here.

`NativeAdmissionIsolation_Tests` characterizes the existing process-global registry
gate with two different databases. A forced identity-mutex wait for A delays B's
open and final release for a measured 250 ms observation interval; B's raw native
lock remains held. Releasing A's mutex permits all operations and both cold logical
checks. This is a bounded head-of-line blocking measurement, not an isolation or
throughput guarantee. Narrowing the gate requires new retain/release/replacement
ordering evidence; the test does not change that protocol or hide its lifecycle cost.

The migration crash/partial-I/O harness retains failed child stdout/stderr, requested
arguments, observed stage/fault markers, runtime/architecture, durations and exit
status. Databases still run on the original system-temp volume. Only after the child
exits are complete fixture trees and hashes copied to failure artifacts; originals
are retained if copying fails. CI exercises timeout, nonzero/invalid fault exits,
missing markers, successful cleanup and failed artifact copying with real children.
The actual migration timeout remains 90 seconds and fault/recovery assertions remain
unchanged. Retention improves future diagnosis; it does not explain the historical
Windows timeout whose evidence was discarded.

## Historical failure and oracle checks

The inspected baseline was `b3e03caef74741da2b1370041a86359c4ec2d1cf`.
Three independent aspect reviews examined ownership, replacement and native
identity; their agreement was not counted as experimental evidence.

The native-identity review reproduced an actual lost acknowledged insert through
two directory bind mounts on that revision: both Shared transactions overlapped,
both commits returned, but cold state omitted one committed row. A same-path
control serialized the writers. The production validation scenario also fails
on a production build of that exact revision with `UNSAFE_ALIAS_ADMITTED`, while
the fixed build passes the identical scenario. This historical PR commit is used
because the new native protocol had not yet reached `dev` or a published package.

Separate deliberately unsafe variants validate the oracles; they are not claimed
as historical bugs:

| Unsafe variant | Required discriminating failure |
| --- | --- |
| Enforce path equality only in the local registry; omit OS path claims | Same-process alias rejection passes, but the child-process alias probe reports `UNSAFE_ALIAS_ADMITTED`. |
| Close the native handle on nonfinal release while leaving the registry populated | Raw ownership assertion fails while a published reference survives, for Direct and Shared. |
| Change critical admission finalization to ordinary finalization | Ordinary buffered-owner probe observes the missing native lock, for both allocation orders. |
| Omit candidate admission | All sixteen handoff cases fail the candidate's raw lock assertion. |
| Release the source immediately after candidate publication | Eight postpublication cases fail the source's raw lock assertion; earlier boundary cases still pass. |

Each mutation campaign runs a passing control, the unsafe variant with a required
named failure (not timeout/build failure), and a restored passing control. Logs,
TRX results, source hashes and exact commands are retained with the task evidence.
The original finalizer test's local registry rejection was demonstrated to pass
even after intentionally closing the native handle; the strengthened probe closes
that specific blind spot. No tests, timeouts or existing failure cases were removed.

## Repeatable execution and finite gate

```sh
dotnet build tools/NativeAdmissionValidation -c Release -p:TestingEnabled=false
dotnet build LiteDB.sln -c Release -p:TestingEnabled=true
dotnet test LiteDB.Tests -c Release -f net8.0 -p:TestingEnabled=true --no-build --settings tests.runsettings --filter 'FullyQualifiedName~NativeAdmission'
bash scripts/test-native-admission-glibc.sh
```

The last command builds the glibc 2.31 container, verifies runtime/architecture,
runs admission/rebuild suites and executes the production bind-mount scenario.
It needs Docker, not a privileged container or nested VM. Full CI runs it on
standard Ubuntu x64/ARM64 runners. Native unit/process tests also execute in the
existing Windows and macOS matrix. Production validation outputs are packaged
before test-hook assemblies are restored, and the runner rejects hook-enabled DLLs.

The finite gate is: the five rules above, their selected scenarios and unsafe
variants, then full CI/fuzz/compatibility on the final PR head. The PR description
records that exact SHA and run links; an earlier green head does not satisfy it.
Revalidate after a relevant base/head change. Historical bad-state comparison is
one-time evidence; the regression scenario remains in CI. Mutation runs remain
bounded local audit tools rather than adding five extra builds to every PR.

Limits: native queries establish exclusion at the observed boundaries, not every
possible schedule. The fault models are API/I/O exceptions, process death and
managed finalization; no hardware power-loss claim is made. Repeated marker refusal
is not described as execution of interrupted automatic recovery. The existing
marker protocol requires a complete matching recovery bundle. Fingerprint
collision resistance, qualified filesystems, stable namespace during use and a
shared named-mutex namespace remain assumptions. File-only bind mounts and moving
only the data file cannot establish historical WAL pairing. Separate isolated
container mutex namespaces require a different operation-coordination contract.
