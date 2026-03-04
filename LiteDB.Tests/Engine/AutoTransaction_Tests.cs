using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using FluentAssertions;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Tests.Utils;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class AutoTransaction_Tests
    {
        private static readonly FieldInfo EngineField = typeof(LiteDatabase).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly MethodInfo AutoTransactionMethod = typeof(LiteEngine).GetMethod("AutoTransaction", BindingFlags.NonPublic | BindingFlags.Instance)!;

        [Fact]
        public void Nested_AutoTransaction_should_not_release_outer_transaction_or_mask_exception()
        {
            using var db = DatabaseFactory.Create(connectionString: "filename=:memory:");
            var engine = (LiteEngine)EngineField.GetValue(db)!;

            Action act = () =>
            {
                ExecuteAutoTransaction<int>(engine, _ =>
                {
                    ExecuteAutoTransaction<int>(engine, __ => throw new InvalidOperationException("boom"));
                    return 0;
                });
            };

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("boom");

            db.BeginTrans().Should().BeTrue();
            db.Rollback().Should().BeTrue();
        }

        private static T ExecuteAutoTransaction<T>(LiteEngine engine, Func<TransactionService, T> action)
        {
            var method = AutoTransactionMethod.MakeGenericMethod(typeof(T));
            try
            {
                return (T)method.Invoke(engine, new object[] { action })!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // Unreachable, but required by compiler.
            }
        }
    }
}
