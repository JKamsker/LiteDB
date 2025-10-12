using System;
using LiteDB;
using LiteDB.Plugins;
using System.Collections.Generic;

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
                    return mapper is null
                        ? new LiteDatabase(connectionString ?? ":memory:", plugins: plugins)
                        : new LiteDatabase(connectionString ?? ":memory:", mapper, plugins: plugins);

                case TestDatabaseType.Disk:
                    if (string.IsNullOrWhiteSpace(connectionString))
                    {
                        throw new ArgumentException("Disk databases require a connection string.", nameof(connectionString));
                    }

                    return mapper is null
                        ? new LiteDatabase(connectionString, plugins: plugins)
                        : new LiteDatabase(connectionString, mapper, plugins: plugins);

                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }
}
