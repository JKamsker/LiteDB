using LiteDB.Tests.Concurrency.LifetimeModel.Direct;
using Xunit;
using Xunit.Abstractions;

namespace LiteDB.Tests.Concurrency.LifetimeModel.Pr133
{
    /// <summary>
    /// Row 13 proof, re-runnable: the generic Direct alphabet and properties, applied to PR #133's
    /// engine at the known-bad commit cb36c346e and at its fix 0d5e5effa (proof branch only; the
    /// models mirror code that exists only on the fork).
    /// </summary>
    public class Pr133ProofTests
    {
        private readonly ITestOutputHelper _output;

        public Pr133ProofTests(ITestOutputHelper output) => _output = output;

        private ModelRunResult Run(string commit, uint seed)
        {
            var options = new ModelRunOptions { Seed = seed, Strategy = ExplorationStrategy.Prioritization }.WithEnvironment();
            var result = ModelRunner.Run($"row13-proof-{commit}", () => new Pr133EngineModel(commit, new DirectScenario()), options);
            _output.WriteLine(result.Summary);
            return result;
        }

        [Fact]
        public void Known_bad_close_fence_deadlocks_an_active_operation_that_depends_on_fresh_work()
        {
            var result = this.Run("cb36c346e", 1);
            Assert.True(result.BugFound, result.Summary);
            Assert.Contains("Liveness violated", result.Bug);
            Assert.Contains("CallbackDependency", result.Bug);
            Assert.Contains("OperationLifetime.cs:30 Enter waits while maintenance holds or is queued", result.Bug);
            Assert.Contains("OperationLifetime.cs:93 Exclusive waits for active operations and dependencies to drain", result.Bug);
        }

        [Theory]
        [InlineData(1u)]
        [InlineData(2u)]
        [InlineData(3u)]
        public void Fix_keeps_the_lifetime_properties(uint seed)
        {
            var result = this.Run("0d5e5effa", seed);
            Assert.False(result.BugFound, result.Summary + "\n" + result.Bug);
        }
    }
}
