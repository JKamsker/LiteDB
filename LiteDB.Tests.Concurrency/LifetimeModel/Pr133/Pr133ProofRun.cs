using System;
using LiteDB.Tests.Concurrency.LifetimeModel.Direct;
using Xunit;
using Xunit.Abstractions;

namespace LiteDB.Tests.Concurrency.LifetimeModel.Pr133
{
    /// <summary>
    /// Proof harness (row 13): the PR #133 Direct engine model under the unchanged generic scenario,
    /// alphabet and properties. Records the outcome; PROOF_MODEL selects the modeled commit.
    /// </summary>
    public class Pr133ProofRun
    {
        private readonly ITestOutputHelper _output;

        public Pr133ProofRun(ITestOutputHelper output) => _output = output;

        [Theory]
        [InlineData(ExplorationStrategy.Random, 1u)]
        [InlineData(ExplorationStrategy.Random, 2u)]
        [InlineData(ExplorationStrategy.Random, 3u)]
        [InlineData(ExplorationStrategy.Prioritization, 1u)]
        [InlineData(ExplorationStrategy.Prioritization, 2u)]
        [InlineData(ExplorationStrategy.Prioritization, 3u)]
        public void Explore(ExplorationStrategy strategy, uint seed)
        {
            var commit = Environment.GetEnvironmentVariable("PROOF_MODEL") ?? "cb36c346e";
            var only = Environment.GetEnvironmentVariable("PROOF_STRATEGY");
            if (only != null && only != strategy.ToString()) return;
            var options = new ModelRunOptions { Seed = seed, Strategy = strategy }.WithEnvironment();
            var result = ModelRunner.Run($"row13-{commit}", () => new Pr133EngineModel(commit, new DirectScenario()), options);
            _output.WriteLine(result.Summary);
            if (result.BugFound) _output.WriteLine(result.Bug.Split('\n')[0]);
        }
    }
}
