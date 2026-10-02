using System;
using System.Collections.Generic;
using LiteDB.Tests.Concurrency.LifetimeModel.Direct;

namespace LiteDB.Tests.Concurrency.LifetimeModel.Pr133
{
    /// <summary>
    /// The Direct engine (<c>LiteEngine</c>) of PR #133 at the modeled commit: the dev model of
    /// per-thread admission through <c>TransactionMonitor</c>/<c>LockService</c>, with every public
    /// call (and every reader advance or dispose, as a continuation) holding an
    /// <see cref="OperationLifetimeModel"/> lease, close and rebuild taking its exclusive lease,
    /// and the fatal stop deferring its close to the last lease. Same scenario generator, alphabet
    /// and properties as <see cref="Direct.DirectEngineModel"/>; the differences are the PR's code.
    /// </summary>
    public sealed partial class Pr133EngineModel : LifetimeModel
    {
        /// <summary>Default pragma TIMEOUT (EnginePragmas: 1 minute).</summary>
        internal const int PragmaTimeout = 60000;

        /// <summary>WalIndexService.READER_WAIT_MILLISECONDS, the close checkpoint's wait for leases.</summary>
        internal const int ReaderWait = 10;

        private static readonly string[] Collections = { "c1", "c2" };

        private readonly DirectMutation _mutation;
        private readonly DirectScenario _scenario;
        private int _generations;
        private int _transactions;

        public Pr133EngineModel(string commit, DirectScenario scenario = null)
            : this(DirectMutation.None, scenario)
        {
            if (commit != "cb36c346e" && commit != "0d5e5effa") throw new ArgumentOutOfRangeException(nameof(commit));
            this.Revision = commit;
            this.Operations = new OperationLifetimeModel(refuseFreshWhileClosing: commit == "0d5e5effa");
        }

        private Pr133EngineModel(DirectMutation mutation = DirectMutation.None, DirectScenario scenario = null)
        {
            _mutation = mutation;
            _scenario = scenario ?? new DirectScenario();
            this.Lifecycle = new Pr133Lifecycle(this);
        }

        internal EngineStateModel State { get; set; }

        internal LockServiceModel Locker { get; set; }

        internal TransactionMonitorModel Monitor { get; set; }

        /// <summary>LiteEngine._operations (LiteEngine.cs:24), one per engine instance across rebuilds.</summary>
        internal OperationLifetimeModel Operations { get; }

        /// <summary>The modeled commit: <c>cb36c346e</c> or <c>0d5e5effa</c>.</summary>
        internal string Revision { get; }

        internal DirectMutation Mutation => _mutation;

        internal Pr133Lifecycle Lifecycle { get; }

        /// <summary>Whether the WAL has content at close (decides whether close checkpoints).</summary>
        internal bool LogHasContent { get; private set; }

        internal new LifetimeLedger Ledger => base.Ledger;

        internal new IModelHost Host => base.Host;

        public override void Build()
        {
            this.Open();
            this.LogHasContent = this.Host.ChooseBool();
            var workers = 1 + this.Host.Choose(2);
            var maintenance = workers == 2 || !_scenario.ConcurrentDispose ? 1 : 1 + this.Host.Choose(2);
            for (var i = 1; i <= workers; i++) this.SpawnWorker($"W{i}", this.Pick(_scenario.Workers));
            for (var i = 1; i <= maintenance; i++)
            {
                // A second maintenance thread is the user's Dispose racing the first one.
                var kind = i == 1 ? this.Pick(_scenario.Maintenance) : OpKind.Close;
                this.Host.Spawn($"M{i}", t => this.Lifecycle.Run(t, kind));
            }
        }

        /// <summary>LiteEngine.Open (LiteEngine.cs:84-194): a new state, lock service and monitor.</summary>
        internal void Open()
        {
            this.State = new EngineStateModel();
            this.OpenLocker();
            this.OpenMonitor();
        }

        internal void OpenLocker() => this.Locker = new LockServiceModel(++_generations, _mutation);

        internal void OpenMonitor() => this.Monitor = new TransactionMonitorModel(this.Locker, _mutation, () => ++_transactions);

        internal string[] Scopes(TransactionMonitorModel monitor) => new[] { "connection", $"gen{monitor.Generation}" };

        private void SpawnWorker(string name, OpKind kind)
        {
            var collection = this.Pick(Collections);
            switch (kind)
            {
                case OpKind.Fresh:
                    this.Host.Spawn(name, t => this.FreshProgram(t, collection));
                    break;
                case OpKind.Nested:
                    this.Host.Spawn(name, t => this.NestedProgram(t, collection));
                    break;
                case OpKind.Owner:
                    this.Host.Spawn(name, t => this.OwnerProgram(t, collection));
                    break;
                case OpKind.Continuation:
                    var handoff = new Handoff<ReaderModel>();
                    var sameThread = this.Host.ChooseBool();
                    this.Host.Spawn(name, t => this.ReaderOpenProgram(t, handoff, sameThread));
                    if (!sameThread) this.Host.Spawn(name + "-dispose", t => this.ReaderDisposeProgram(t, handoff));
                    break;
                case OpKind.CallbackDependency:
                    var dependency = new Handoff<bool>();
                    var dependentCollection = this.Pick(Collections);
                    this.Host.Spawn(name, t => this.CallbackProgram(t, collection, dependency));
                    this.Host.Spawn(name + "-dependent", t => this.DependentProgram(t, dependentCollection, dependency));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public override string DescribeState() => $"{this.State}; {this.Operations}; {this.Locker}; {this.Monitor}";

        /// <summary>A value passed between two model threads (a reader handed over, a request and its completion).</summary>
        internal sealed class Handoff<T>
        {
            public bool Started { get; set; }

            public bool Finished { get; set; }

            public bool Cancelled { get; set; }

            public T Value { get; set; }
        }
    }
}
