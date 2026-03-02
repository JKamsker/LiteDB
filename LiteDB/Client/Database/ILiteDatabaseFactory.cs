using System;

namespace LiteDB
{
    public interface ILiteDatabaseFactory : IDisposable
    {
        ILiteDatabase CreateDatabase();
    }
}

