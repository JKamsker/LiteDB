# DisposedBoundObject

A disposed bound reader or enumerator must reject use before entering the transaction operation, so earlier writes stay active and can commit. The original throws NullReferenceException inside the operation and marks the handle Failed. The fixed oracle also exercises an enumerator and twice cold-reopens the committed records, secondary index and untouched sentinel.

Disposed children consistently throw ObjectDisposedException, including after their transaction completes; this rejection takes precedence over the terminal handle error.

The black-box comparison pins PR #133 commit `560529066aeda64c24d5cdd32d34a8aca12695ae`. Package exit 0 plus `REGRESSION_REPRODUCED` proves the original failure; source exit 10 plus `FIXED_STATE_VERIFIED` proves the stated oracle completed. Unexpected errors exit 2 and cannot pass either expectation. This covers ordinary concurrency/disposal and synchronous exception faults, not process death or power loss.
