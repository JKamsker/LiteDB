# Refused leased-reader self-disposal must remain retryable

Public black-box proof of [review P2](https://github.com/JKamsker/LiteDB/pull/133#pullrequestreview-5366098967), pinned to actual reviewed commit `f95f0b0cd59d35d285ac72183589975751e35229`.

A `ReadTransform` callback reached only after Query and the first reader advance attempts to dispose its own leased reader. The callback catches the specific `InvalidOperationException` refusal. After it unwinds the proof retries reader disposal, ends any parent-core holder, and disposes the Shared connection. It keeps all apparently disposed public objects strongly reachable through a fresh raw Direct-engine admission probe, preventing finalizer cleanup from rescuing the known-bad case.

The known-bad result is specifically `DatabaseAdmissionException` with the inner incompatible-local-access diagnostic after that cleanup. Arbitrary open failures or timeouts are errors. The read-only Shared case exercises QuerySnapshot; a writable Shared connection retaining an independent FOR UPDATE reader steers the ordinary leased query through QueryCore. Both paths run with true null-password plain and nonempty-password encrypted files, plus normal non-self-disposing controls.

The fixed result admits Direct, permits a separate Shared writer to commit, and twice cold-opens exact records/index plans and an unrelated sentinel. Strict outcomes: package `0`/`BUG_REPRODUCED`, source `10`/`VERIFIED_FIXED`; unexpected failures exit `2`. No product hooks or private reflection are used, and no corruption or power-loss guarantee is inferred from this resource-retention proof.
