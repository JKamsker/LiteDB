using System;
using FluentAssertions;
using LiteDB.Plugins;
using Xunit;

namespace LiteDB.Tests.Plugins
{
    public class EnsureIndexInterceptorContractTests
    {
        [Fact]
        public void Interceptor_that_returns_true_must_set_result_or_execute_default()
        {
            using var db = new LiteDatabase(":memory:", plugins: new[] { new BadEnsureIndexInterceptorPlugin() });
            var collection = db.GetCollection<TestDocument>("docs");

            Action act = () => collection.EnsureIndex("name_idx", x => x.Name);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*EnsureIndex interceptor*returned true*");
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public string Name { get; set; }
        }

        private sealed class BadEnsureIndexInterceptorPlugin : ILitePlugin
        {
            public void Initialize(LiteDatabase database, ILitePluginContext context)
            {
                context.EnsureIndexInterceptors.Add(new BadInterceptor());
            }

            private sealed class BadInterceptor : IEnsureIndexInterceptor
            {
                public bool TryHandleEnsureIndex(EnsureIndexContext context)
                {
                    return true;
                }
            }
        }
    }
}

