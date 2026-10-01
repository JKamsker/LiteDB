# Explicit transaction thread ownership

`BeginTrans`, all operations in the transaction, and `Commit` or `Rollback` must
run synchronously on the same managed thread. Do not put `await` inside that block.
A nested `BeginTrans` returns false and joins the existing transaction for that
connection context and thread; it does not create an independent transaction or
savepoint.

File-backed Direct `LiteDatabase` instances can share a storage host while owning
separate engine contexts. A transaction in A never includes B's work, even on the
same thread, and B cannot commit or roll back A's transaction. Collection write
locks belong to transactions: a second context on the same thread attempting to
write a collection locked by the first fails immediately. It can write a different
collection when that collection's lock is available. See
[Direct engine ownership](direct-engine-ownership.md).

When the calling thread has no transaction while another thread in the same
context has an active explicit transaction, `Commit` throws a descriptive
`LiteException` and leaves the owner's transaction untouched. `Rollback` returns
false in that situation:
it belongs in `catch`/`finally` blocks, where throwing would replace the error
being handled. With no transaction in that context, both methods return false,
even if another context has an explicit transaction.

A failed operation rolls back the calling context and thread's explicit
transaction. That thread then legitimately has no transaction, so its next
`Commit` or `Rollback` returns false even while other threads in that context have
explicit transactions open. A later `Commit` without a new `BeginTrans` is treated
as foreign again.

This guard cannot distinguish two logical tasks that reuse the same context and
thread while a transaction is open. It does not make explicit transactions
async-safe. Keep the whole block synchronous, use a dedicated thread when
necessary, or rely on the
per-operation automatic transactions. An explicit transaction-handle API remains
separate future work. Transaction-owned collection locks allow context cleanup
from another thread; they do not permit foreign-thread `Commit` or `Rollback`.

In shared mode, if the owner thread exits and abandons its named mutex, the next
operation on that instance rejects the abandoned transaction and discards its uncommitted state.
The database can then be disposed normally. Foreign completion while the owner
is still alive preserves that owner's transaction: `Commit` throws, `Rollback` returns false.
