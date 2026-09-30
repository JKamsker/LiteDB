# Handle callback self-wait

A Direct transaction owns the rows collection, then a mapper callback performs an
independent ordinary write to that same collection. The ordinary write must reject
the self-wait immediately; a different collection still commits independently.
The handle remains active and is rolled back, and two cold reopens check the
indexed committed row and independent/sentinel collections.

The public black-box reproducer uses a four-second TIMEOUT. An exact LOCK_TIMEOUT
after at least three seconds reproduces the old waiting behavior. The fixed oracle
requires rejection within one second; the ambiguous interval or any other failure
exits 2 and cannot pass. The permanent tests use the existing BeforeWait observer
instead of timing and also cover encrypted files and sequential thread handoff.

Known-bad PR commit: eb01f346eb7d8d51925a45c2f87ef40e1c3984ee. Strict expected
exits/markers distinguish reproduction (0) and completed verification (10).
This is exception/concurrency evidence, not a power-loss test.
