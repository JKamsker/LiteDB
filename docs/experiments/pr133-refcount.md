# Atomic session admission experiment

One CAS word combines closed admission and the active synchronous operation count. Thread-local active-scope metadata replaces the per-session monitor/dictionary on ordinary entry and exit. Closing still uses the existing lock, independent scheduler and timeout. Lease release is synchronous and ordered; returned readers do not carry a session lease. This changes no persistent format.

Baseline: c8c0cfab623a22880b1d71eb966b09e89f9153b9.

Cross-variant results and evidence are linked from JKamsker/LiteDB PR #133.
