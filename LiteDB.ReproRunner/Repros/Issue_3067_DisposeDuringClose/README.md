# DisposeDuringClose

A read-transform barrier holds a live transaction call while session close revokes admission. Dispose must hand cleanup to session close without replacing a user failure with an overlapping-use exception. The original throws InvalidOperationException; the fixed oracle drains both tasks, requires RolledBack, and twice cold-reopens the indexed committed sentinel state. The permanent tests additionally stop close dispatch and block the rollback worker at exact existing hooks.

The black-box comparison pins PR #133 commit `560529066aeda64c24d5cdd32d34a8aca12695ae`. Package exit 0 plus `REGRESSION_REPRODUCED` proves the original failure; source exit 10 plus `FIXED_STATE_VERIFIED` proves the stated oracle completed. Unexpected errors exit 2 and cannot pass either expectation. This covers ordinary concurrency/disposal and synchronous exception faults, not process death or power loss.
