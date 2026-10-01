using System; using System.IO; using System.Collections.Generic; using LiteDB; using LiteDB.Engine;
public sealed class MockDatabase : ILiteDatabase {
public BsonMapper Mapper => throw new NotSupportedException();
public ILiteStorage<string> FileStorage => throw new NotSupportedException();
public ILiteCollection<T> GetCollection<T>(string name, BsonAutoId autoId = BsonAutoId.ObjectId) => throw new NotSupportedException();
public ILiteCollection<T> GetCollection<T>() => throw new NotSupportedException();
public ILiteCollection<T> GetCollection<T>(BsonAutoId autoId) => throw new NotSupportedException();
public ILiteCollection<BsonDocument> GetCollection(string name, BsonAutoId autoId = BsonAutoId.ObjectId) => throw new NotSupportedException();
public bool BeginTrans() => throw new NotSupportedException();
public bool Commit() => throw new NotSupportedException();
public bool Rollback() => throw new NotSupportedException();
public ILiteStorage<TFileId> GetStorage<TFileId>(string filesCollection = "_files", string chunksCollection = "_chunks") => throw new NotSupportedException();
public IEnumerable<string> GetCollectionNames() => throw new NotSupportedException();
public bool CollectionExists(string name) => throw new NotSupportedException();
public bool DropCollection(string name) => throw new NotSupportedException();
public bool RenameCollection(string oldName, string newName) => throw new NotSupportedException();
public IBsonDataReader Execute(TextReader commandReader, BsonDocument parameters = null) => throw new NotSupportedException();
public IBsonDataReader Execute(string command, BsonDocument parameters = null) => throw new NotSupportedException();
public IBsonDataReader Execute(string command, params BsonValue[] args) => throw new NotSupportedException();
public void Checkpoint() => throw new NotSupportedException();
public long Rebuild(RebuildOptions options = null) => throw new NotSupportedException();
public BsonValue Pragma(string name) => throw new NotSupportedException();
public BsonValue Pragma(string name, BsonValue value) => throw new NotSupportedException();
public int UserVersion { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
public TimeSpan Timeout { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
public bool UtcDate { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
public long LimitSize { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
public int CheckpointSize { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
public Collation Collation => throw new NotSupportedException();
public void Dispose() {}
}
