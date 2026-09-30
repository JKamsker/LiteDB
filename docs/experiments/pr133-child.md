# Reusable Shared holder and child-wrapper experiment

Extends holder-thread reuse with a cached child SharedEngine per originating SharedEngine. The storage core still closes/reopens on every transaction: retaining a coherent core across external writers is a separate protocol change. Cached children retain only detached settings and weak application policy callbacks, release native writer ownership each time, and are disposed with the parent. The holder points weakly back to the cache owner so abandonment cannot be prevented. A process test verifies external writers make progress and the reused wrapper observes their commits, including encrypted files.

Baseline: c8c0cfab623a22880b1d71eb966b09e89f9153b9.

Cross-variant results and evidence are linked from JKamsker/LiteDB PR #133.
