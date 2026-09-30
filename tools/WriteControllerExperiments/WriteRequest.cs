using LiteDB;

namespace WriteControllerExperiments;

// An immutable command, not an application callback or a transaction handle.
internal sealed record WriteRequest(long Id, int Value)
{
    internal const string Payload = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    internal BsonDocument Document() => new() { ["_id"] = Id, ["value"] = Value, ["payload"] = Payload };
}

internal sealed class BatchRejectedException(Exception inner) : Exception("Entire batch rolled back.", inner);
internal sealed class OutcomeUnknownException(Exception inner) : IOException("Commit outcome unknown; reconcile IDs before retrying.", inner);
