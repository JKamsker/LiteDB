using System;
using FluentAssertions;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Plugins.Query;
using Xunit;

namespace LiteDB.Tests.QueryTest
{
    public class PluginPlanningCostTests
    {
        [Fact]
        public void Plugin_cost_model_should_receive_consumed_term()
        {
            var plugin = new CostModelTestPlugin();

            using var db = new LiteDatabase(":memory:", plugins: new[] { plugin });
            var collection = db.GetCollection<TestDocument>("docs");

            collection.Insert(new TestDocument { Id = 1, Name = "Alice" });
            collection.EnsureIndex("name_idx", x => x.Name).Should().BeTrue();

            var predicate = Query.EQ("Name", "Alice", db.Services.ExpressionRegistry);

            var plan = collection.Query()
                .Where(predicate)
                .GetPlan();

            plugin.CapturedCostContext.Should().NotBeNull();
            ReferenceEquals(plugin.CapturedCostContext.Query, plugin.ConsumedTerm).Should().BeTrue();
            plugin.CapturedCostContext.IndexMetadata.Count.Should().Be(0);
            plugin.CapturedCostContext.EstimatedDocumentCount.Should().BeGreaterThan(0);

            plan["index"]["cost"].AsInt32.Should().Be(123);
        }

        [Fact]
        public void Plugin_planner_cost_fallback_should_use_selected_index_definition()
        {
            var plugin = new FallbackCostTestPlugin();

            using var db = new LiteDatabase(":memory:", plugins: new[] { plugin });
            var collection = db.GetCollection<TestDocument>("docs");

            collection.Insert(new TestDocument { Id = 1, Name = "Alice" });
            collection.EnsureIndex("name_idx", x => x.Name).Should().BeTrue();

            var predicate = Query.EQ("Name", "Alice", db.Services.ExpressionRegistry);

            var plan = collection.Query()
                .Where(predicate)
                .GetPlan();

            plan["index"]["name"].AsString.Should().Be("name_idx");
            plan["index"]["cost"].AsInt32.Should().Be(10);
        }

        [Fact]
        public void Plugin_planner_cost_fallback_should_not_fallback_to_pk_when_selected_index_is_missing()
        {
            var plugin = new MissingIndexFallbackTestPlugin();

            using var db = new LiteDatabase(":memory:", plugins: new[] { plugin });
            var collection = db.GetCollection<TestDocument>("docs");

            collection.Insert(new TestDocument { Id = 1, Name = "Alice" });
            collection.EnsureIndex("name_idx", x => x.Name).Should().BeTrue();

            var predicate = Query.EQ("Name", "Alice", db.Services.ExpressionRegistry);

            var plan = collection.Query()
                .Where(predicate)
                .GetPlan();

            plan["index"]["name"].AsString.Should().Be("missing_idx");
            plan["index"]["cost"].AsInt32.Should().Be(-1);
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public string Name { get; set; }
        }

        private sealed class CostModelTestPlugin : ILitePlugin
        {
            internal const string PluginId = "LiteDB.Tests.CostModel";
            internal const string IndexKind = "test.kind";

            internal BsonExpression ConsumedTerm { get; private set; }

            public QueryCostContext CapturedCostContext { get; private set; }

            public void Initialize(LiteDatabase database, ILitePluginContext context)
            {
                context.QueryCostModels.Register(new QueryCostModelRegistration(
                    PluginId,
                    IndexKind,
                    costContext =>
                    {
                        CapturedCostContext = costContext;
                        return ReferenceEquals(costContext.Query, ConsumedTerm) ? 123 : 456;
                    }));

                context.QueryPlanner.AddRule(new CostModelPlannerRule(this), order: 0);
            }

            private sealed class CostModelPlannerRule : IQueryPlanningRule
            {
                private readonly CostModelTestPlugin _plugin;

                public CostModelPlannerRule(CostModelTestPlugin plugin)
                {
                    _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
                }

                public bool TryRewrite(QueryPlanningContext context)
                {
                    var snapshot = context.SnapshotContext as Snapshot;
                    var collection = snapshot?.CollectionPage;
                    var index = collection?.GetCollectionIndex("name_idx");

                    if (index == null)
                    {
                        return false;
                    }

                    foreach (var term in context.Terms)
                    {
                        if (term == null || !term.IsPredicate)
                        {
                            continue;
                        }

                        if (term.Left == null || term.Right == null)
                        {
                            continue;
                        }

                        if (!string.Equals(term.Left.Source, index.Expression, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!term.Right.IsValue)
                        {
                            continue;
                        }

                        var value = term.Right.ExecuteScalar(snapshot.Collation);

                        _plugin.ConsumedTerm = term;

                        context.UseIndex(
                            new IndexEquals(index.Name, value),
                            index.Expression,
                            consumedTerms: new[] { term },
                            pluginId: PluginId,
                            pluginIndexKind: IndexKind);

                        return true;
                    }

                    return false;
                }
            }
        }

        private sealed class FallbackCostTestPlugin : ILitePlugin
        {
            public void Initialize(LiteDatabase database, ILitePluginContext context)
            {
                context.QueryPlanner.AddRule(new FallbackCostPlannerRule(), order: 0);
            }

            private sealed class FallbackCostPlannerRule : IQueryPlanningRule
            {
                public bool TryRewrite(QueryPlanningContext context)
                {
                    var snapshot = context.SnapshotContext as Snapshot;
                    var collection = snapshot?.CollectionPage;
                    var index = collection?.GetCollectionIndex("name_idx");

                    if (index == null)
                    {
                        return false;
                    }

                    foreach (var term in context.Terms)
                    {
                        if (term == null || !term.IsPredicate)
                        {
                            continue;
                        }

                        if (term.Left == null || term.Right == null)
                        {
                            continue;
                        }

                        if (!string.Equals(term.Left.Source, index.Expression, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!term.Right.IsValue)
                        {
                            continue;
                        }

                        var value = term.Right.ExecuteScalar(snapshot.Collation);

                        context.UseIndex(
                            new IndexEquals(index.Name, value),
                            index.Expression,
                            consumedTerms: new[] { term });

                        return true;
                    }

                    return false;
                }
            }
        }

        private sealed class MissingIndexFallbackTestPlugin : ILitePlugin
        {
            public void Initialize(LiteDatabase database, ILitePluginContext context)
            {
                context.QueryPlanner.AddRule(new PlannerRule(), order: 0);
            }

            private sealed class PlannerRule : IQueryPlanningRule
            {
                public bool TryRewrite(QueryPlanningContext context)
                {
                    var snapshot = context.SnapshotContext as Snapshot;
                    var indexExpression = snapshot?.CollectionPage?.GetCollectionIndex("name_idx")?.Expression ?? "$.Name";
                    var collation = snapshot?.Collation;

                    if (collation == null)
                    {
                        return false;
                    }

                    foreach (var term in context.Terms)
                    {
                        if (term == null || !term.IsPredicate)
                        {
                            continue;
                        }

                        if (term.Left == null || term.Right == null)
                        {
                            continue;
                        }

                        if (!term.Right.IsValue)
                        {
                            continue;
                        }

                        var value = term.Right.ExecuteScalar(collation);

                        context.UseIndex(
                            new IndexEquals("missing_idx", value),
                            indexExpression,
                            consumedTerms: Array.Empty<BsonExpression>());

                        return true;
                    }

                    return false;
                }
            }
        }
    }
}
