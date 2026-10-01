# Native admission performance follow-up

The [review](https://github.com/JKamsker/LiteDB/pull/132#issuecomment-5883590064)
requests separate optimization work with production before/after evidence and
unchanged safety oracles. These are pending optimizations, not claims about the
current implementation.

At revision f75624e, paired production runs against d1189a5 kept retained Shared
point/scan performance approximately unchanged. Windows mixed work cost about
12% more; open/use/close cost about 2.05x on .NET 8 and 1.95x on .NET 10. This
points to admission setup/teardown and per-operation engine costs; it does not
establish an inherent native-lock cost. Keep the earlier retained-sidecar guard
measurements from #3023 as another comparator.

1. Replace the retained guard's global-gate fault read with an atomic/volatile
   fault publication protocol. Failed conversion must invalidate every local
   user before further storage access.
2. Partition or narrow registry serialization while preserving lock ordering,
   identity revalidation, final release and replacement. Keep the existing
   contention/exclusion oracle and change its progress expectation explicitly.
3. Reuse already-established primary identity/volume qualification for helper
   descriptors, while still proving the descriptor names the expected file.
4. Cache enforcement capability only by a stable mount/volume identity. Mount
   changes and new remote backing must never inherit qualification blindly.
5. Measure path-fingerprint kernel calls before reducing them; retain the same
   canonical data/WAL/mutex exclusion invariant and alias negative controls.
6. Investigate bounded bootstrap-mutex handle caching. Ownership remains scoped
   to the acquiring thread, with abandoned-owner semantics; mutexes must not
   become lifetime admission locks.
7. Add production phase measurements for canonicalization, identity/volume
   checks, path and identity waits, native protocol calls, enforcement probing,
   retained leases and final release. Report whole open/use/close alongside
   phases for Direct and Shared on Windows/Linux, including Shared point reads
   with mapped reads disabled.

Each change needs independent before/after production runs and the admission,
replacement, crash, ownership and alias safety suites. The explicit host-local
fallback has additional setup and protected-reader costs; report it separately
from native admission. Do not weaken safety assertions to meet a timing budget.
