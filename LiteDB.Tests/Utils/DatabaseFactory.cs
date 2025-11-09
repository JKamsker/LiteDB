using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LiteDB;
using LiteDB.Plugins;

namespace LiteDB.Tests.Utils
{
    public enum TestDatabaseType
    {
        Default,
        InMemory,
        Disk
    }

    public static class DatabaseFactory
    {
        public static LiteDatabase Create(
            TestDatabaseType type = TestDatabaseType.Default,
            string connectionString = null,
            BsonMapper mapper = null,
            IEnumerable<ILitePlugin> plugins = null)
        {
            switch (type)
            {
                case TestDatabaseType.Default:
                case TestDatabaseType.InMemory:
                    if (mapper is null)
                    {
                        return new LiteDatabase(connectionString ?? ":memory:", plugins: plugins);
                    }

                    return new LiteDatabase(connectionString ?? ":memory:", mapper, plugins);

                case TestDatabaseType.Disk:
                    if (string.IsNullOrWhiteSpace(connectionString))
                    {
                        throw new ArgumentException("Disk databases require a connection string.", nameof(connectionString));
                    }

                    if (mapper is null)
                    {
                        return new LiteDatabase(connectionString, plugins: plugins);
                    }

                    return new LiteDatabase(connectionString, mapper, plugins);

                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        public static LiteDatabaseGroup CreateMany(params DatabaseFactoryOptions[] configurations)
        {
            if (configurations == null || configurations.Length == 0)
            {
                throw new ArgumentException("At least one database configuration must be supplied.", nameof(configurations));
            }

            var instances = new List<LiteDatabase>(configurations.Length);

            try
            {
                foreach (var configuration in configurations)
                {
                    var options = configuration ?? DatabaseFactoryOptions.InMemory();
                    instances.Add(Create(options.Type, options.ConnectionString, options.Mapper, options.Plugins));
                }

                return new LiteDatabaseGroup(instances);
            }
            catch
            {
                foreach (var database in instances)
                {
                    database?.Dispose();
                }

                throw;
            }
        }
    }

    public sealed class DatabaseFactoryOptions
    {
        public DatabaseFactoryOptions(
            TestDatabaseType type = TestDatabaseType.Default,
            string connectionString = null,
            BsonMapper mapper = null,
            IEnumerable<ILitePlugin> plugins = null)
        {
            Type = type;
            ConnectionString = connectionString;
            Mapper = mapper;
            Plugins = plugins;
        }

        public TestDatabaseType Type { get; }

        public string ConnectionString { get; }

        public BsonMapper Mapper { get; }

        public IEnumerable<ILitePlugin> Plugins { get; }

        public static DatabaseFactoryOptions InMemory(
            string connectionString = null,
            BsonMapper mapper = null,
            IEnumerable<ILitePlugin> plugins = null)
        {
            return new DatabaseFactoryOptions(TestDatabaseType.InMemory, connectionString, mapper, plugins);
        }

        public static DatabaseFactoryOptions Disk(
            string connectionString,
            BsonMapper mapper = null,
            IEnumerable<ILitePlugin> plugins = null)
        {
            return new DatabaseFactoryOptions(TestDatabaseType.Disk, connectionString, mapper, plugins);
        }
    }

    public sealed class LiteDatabaseGroup : IReadOnlyList<LiteDatabase>, IDisposable
    {
        private readonly List<LiteDatabase> _instances;
        private readonly ReadOnlyCollection<LiteDatabase> _readonlyInstances;
        private bool _disposed;

        public LiteDatabaseGroup(IEnumerable<LiteDatabase> databases)
        {
            if (databases == null)
            {
                throw new ArgumentNullException(nameof(databases));
            }

            _instances = new List<LiteDatabase>();

            foreach (var database in databases)
            {
                if (database == null)
                {
                    throw new ArgumentException("Database groups cannot contain null references.", nameof(databases));
                }

                _instances.Add(database);
            }

            if (_instances.Count == 0)
            {
                throw new ArgumentException("At least one database instance must be supplied.", nameof(databases));
            }

            _readonlyInstances = _instances.AsReadOnly();
        }

        public LiteDatabase Primary => _instances[0];

        public IReadOnlyList<LiteDatabase> Instances => _readonlyInstances;

        public LiteDatabase this[int index] => _instances[index];

        public int Count => _instances.Count;

        public IEnumerator<LiteDatabase> GetEnumerator() => _instances.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            for (var i = 0; i < _instances.Count; i++)
            {
                _instances[i]?.Dispose();
            }

            _disposed = true;
        }
    }
}
