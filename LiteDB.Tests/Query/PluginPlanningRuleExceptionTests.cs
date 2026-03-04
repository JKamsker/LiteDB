using System;
using System.Collections.Generic;
using FluentAssertions;
using LiteDB.Engine;
using LiteDB.Plugins;
using Xunit;

namespace LiteDB.Tests.QueryTest
{
    public class PluginPlanningRuleExceptionTests
    {
        [Fact]
        public void Planner_should_log_and_continue_when_rule_throws()
        {
            var logger = new CapturingLogger();

            using var db = new LiteDatabase(
                ":memory:",
                new LiteDatabaseOptions
                {
                    Logger = logger,
                    Plugins = new[] { new PlanningPlugin() }
                });

            var collection = db.GetCollection<TestDocument>("docs");

            collection.Insert(new TestDocument { Id = 1, Name = "Alice" });
            collection.EnsureIndex("name_idx", x => x.Name).Should().BeTrue();

            var predicate = Query.EQ("Name", "Alice", db.Services.ExpressionRegistry);

            var plan = collection.Query()
                .Where(predicate)
                .GetPlan();

            plan["index"]["name"].AsString.Should().Be("name_idx");

            logger.Entries.Should().Contain(entry =>
                entry.Level == LogLevel.Error &&
                entry.Message.IndexOf("Query planning rule", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public string Name { get; set; }
        }

        private sealed class PlanningPlugin : ILitePlugin
        {
            public void Initialize(LiteDatabase database, ILitePluginContext context)
            {
                context.QueryPlanner.AddRule(new ThrowingRule(), order: 0);
                context.QueryPlanner.AddRule(new SelectingRule(), order: 0);
            }

            private sealed class ThrowingRule : IQueryPlanningRule
            {
                public bool TryRewrite(QueryPlanningContext context)
                {
                    throw new InvalidOperationException("Planner rule failure.");
                }
            }

            private sealed class SelectingRule : IQueryPlanningRule
            {
                public bool TryRewrite(QueryPlanningContext context)
                {
                    var snapshot = context.SnapshotContext as Snapshot;
                    var index = snapshot?.CollectionPage?.GetCollectionIndex("name_idx");

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

        private sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new List<(LogLevel, string)>();

            public void Write(LogLevel level, string message, Exception exception = null)
            {
                Entries.Add((level, message));
            }
        }
    }
}
