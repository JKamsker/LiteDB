using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class TransactionHandleContext_Tests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Windows_impersonation_is_refused_before_handle_acquisition(bool occupied)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            using var file = new TempFile();
            using var db = TransactionHandleAdmission_Tests.Open(file);
            db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 1 });
            using var owner = occupied ? db.BeginTransaction() : null;
            using (var identity = WindowsIdentity.GetCurrent())
            {
                Action check = () =>
                {
                    using var impersonated = WindowsIdentity.GetCurrent(true);
                    Assert.NotNull(impersonated);
                    Assert.Throws<NotSupportedException>(() => db.BeginTransaction(TimeSpan.Zero));
                };
#if NETFRAMEWORK
                using (identity.Impersonate()) check();
#else
                WindowsIdentity.RunImpersonated(identity.AccessToken, check);
#endif
            }
            owner?.Rollback();
            using var retry = db.BeginTransaction(TimeSpan.Zero);
            Assert.NotNull(retry.GetCollection("sentinel").FindById(1));
            retry.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2 });
            retry.Commit();
            db.Dispose();
            using var cold = new LiteDatabase(file);
            Assert.NotNull(cold.GetCollection("sentinel").FindById(1));
            Assert.NotNull(cold.GetCollection("rows").FindById(2));
        }
    }
}
