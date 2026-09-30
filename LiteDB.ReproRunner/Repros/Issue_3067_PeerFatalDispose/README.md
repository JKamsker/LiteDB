# PeerFatalDispose

Two Direct handles write separate collections. A caller-owned WAL stream throws before writing any bytes for one commit. That handle must report the original IOException and Indeterminate; disposing the other handle must quietly roll back its uncommitted writes, not repeat the peer fatal IOException. Two cold reopens check the complete known committed record/index/sentinel model.

The black-box comparison pins PR #133 commit `560529066aeda64c24d5cdd32d34a8aca12695ae`. Package exit 0 plus `REGRESSION_REPRODUCED` proves the original failure; source exit 10 plus `FIXED_STATE_VERIFIED` proves the stated oracle completed. Unexpected errors exit 2 and cannot pass either expectation. This covers ordinary concurrency/disposal and synchronous exception faults, not process death or power loss.

The same public scenario runs twice: with immediate teardown and with an unrelated read-transform callback keeping the fatal core alive. Both must complete the fixed oracle; a reproduced failure in either variant remains a regression. Permanent fault tests additionally publish the peer latch after cleanup admission and ensure this cleanup's own IOException and INVALID_DATAFILE_STATE still propagate unchanged.
