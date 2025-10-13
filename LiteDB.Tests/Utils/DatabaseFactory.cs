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
    }
}
