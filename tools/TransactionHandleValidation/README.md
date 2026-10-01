# API compatibility and semantic regression proof

The fixture implements the pre-handle `ILiteDatabase` and `ILiteEngine` interfaces,
uses valid synchronous legacy transactions, and preserves caller-owned engine
disposal. It compiles against the real parent DLL, then runs that same precompiled
consumer with the candidate DLL. Unsupported handle providers/decorators are
checked through reflection without compiling against new APIs.

`error` is a semantic regression proof using only the old API: a failing bulk
enumerable followed by failing rollback cleanup must preserve the input failure
and attach the cleanup failure. Parent49c327cf actually executes and fails this
contract; no missing-API compilation failure counts as evidence.

Build both libraries in separate checkouts with **TestingEnabled=true** for the
fault hook, then run:

```sh
python3 scripts/test-transaction-handle-compatibility.py \
  --parent-dll /absolute/parent/LiteDB.dll \
  --candidate-dll /absolute/candidate/LiteDB.dll \
  --output artifacts_temp/transaction-handle-compatibility
```

The script additionally verifies CS0618 fails a warnings-as-errors consumer and
the documented targeted `WarningsNotAsErrors=CS0618` exception permits migration.
Output includes exact binary hashes, commands, exit codes and retained logs.
