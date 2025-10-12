using System;
using System.Collections.Generic;
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
        public static LiteDatabase Create(TestDatabaseType type = TestDatabaseType.Default, string connectionString = null, BsonMapper mapper = null, IEnumerable<ILitePlugin> plugins = null)
        {
            switch (type)
            {
                case TestDatabaseType.Default:
                case TestDatabaseType.InMemory:
                    return new LiteDatabase(connectionString ?? ":memory:", mapper: mapper, plugins: plugins);

                case TestDatabaseType.Disk:
                    if (string.IsNullOrWhiteSpace(connectionString))
                    {
                        throw new ArgumentException("Disk databases require a connection string.", nameof(connectionString));
                    }

                    return new LiteDatabase(connectionString, mapper: mapper, plugins: plugins);

                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }
}
