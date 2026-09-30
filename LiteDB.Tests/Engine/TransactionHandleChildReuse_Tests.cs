#if !NETFRAMEWORK
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LiteDB.Engine;
using LiteDB.Internals;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class TransactionHandleChildReuse_Tests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("secret")]
        public async Task Cached_child_releases_writer_and_reopens_after_external_commit(string password)
        {
            using var file = new TempFile();
            await MvccProcess.Run("seed", file, password);
            var field = typeof(SharedEngine).GetField("_cachedTransactionChild", BindingFlags.NonPublic | BindingFlags.Instance);
            using (var shared = new SharedEngine(new EngineSettings { Filename = file, Password = password }))
            using (var db = new LiteDatabase(shared, disposeOnClose: false))
            {
                using (var tx = db.BeginTransaction())
                {
                    Assert.NotEmpty(tx.GetCollection("docs").FindAll());
                    tx.Commit();
                }
                var cached = field.GetValue(shared);
                Assert.NotNull(cached);
                for (var value = 1; value <= 3; value++)
                {
                    // Must acquire native ownership while our cached child still lives.
                    await MvccProcess.Run("write", file, password, value.ToString());
                    using var tx = db.BeginTransaction();
                    Assert.All(tx.GetCollection("docs").FindAll(), row => Assert.Equal(value, row["value"].AsInt32));
                    Assert.All(tx.GetCollection("cold").FindAll(), row => Assert.Equal(0, row["value"].AsInt32));
                    tx.Commit();
                    Assert.Same(cached, field.GetValue(shared));
                }
            }
            for (var repeat = 0; repeat < 2; repeat++)
            {
                using var cold = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
                Assert.All(cold.GetCollection("docs").FindAll(), row => Assert.Equal(3, row["value"].AsInt32));
                Assert.All(cold.GetCollection("cold").FindAll(), row => Assert.Equal(0, row["value"].AsInt32));
            }
        }
    }
}
#endif
