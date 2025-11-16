using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Plugins;

namespace LiteDB.Plugins.Indexing
{
    /// <summary>
    /// Registry contract used to coordinate plugin-provided custom index strategy descriptors.
    /// </summary>
    public interface ICustomIndexStrategyRegistry
    {
        /// <summary>
        /// Registers the supplied descriptor.
        /// </summary>
        /// <param name="descriptor">Descriptor describing the strategy.</param>
        void Register(CustomIndexStrategyDescriptor descriptor);

        /// <summary>
        /// Attempts to resolve a descriptor by its identifier.
        /// </summary>
        /// <param name="strategyId">The logical strategy identifier.</param>
        /// <param name="descriptor">Receives the descriptor when found.</param>
        /// <returns>True when the descriptor exists.</returns>
        bool TryGet(string strategyId, out CustomIndexStrategyDescriptor descriptor);

        /// <summary>
        /// Resolves a descriptor by its identifier.
        /// </summary>
        /// <param name="strategyId">The logical strategy identifier.</param>
        /// <returns>The registered descriptor.</returns>
        CustomIndexStrategyDescriptor Get(string strategyId);

        /// <summary>
        /// Gets all registered descriptors.
        /// </summary>
        IReadOnlyCollection<CustomIndexStrategyDescriptor> Registered { get; }
    }

    /// <summary>
    /// Delegate invoked when a custom index ensure request is executed.
    /// </summary>
    /// <param name="context">Context describing the ensure operation.</param>
    /// <returns>True when the ensure operation created or updated the index.</returns>
    public delegate bool CustomIndexEnsureDelegate(CustomIndexEnsureContext context);

    /// <summary>
    /// Delegate invoked during query planning to allow the strategy to contribute behaviours.
    /// </summary>
    /// <param name="context">Query planning context.</param>
    public delegate void CustomIndexQueryPlannerDelegate(CustomIndexQueryPlannerContext context);

    /// <summary>
    /// Delegate invoked while orchestrating rebuild flows so the strategy can maintain metadata.
    /// </summary>
    /// <param name="context">Rebuild coordination context.</param>
    public delegate void CustomIndexRebuildDelegate(CustomIndexRebuildContext context);

    /// <summary>
    /// Describes a plugin-managed custom index strategy and its required infrastructure.
    /// </summary>
    public sealed class CustomIndexStrategyDescriptor
    {
        public CustomIndexStrategyDescriptor(
            string pluginId,
            string strategyId,
            CustomIndexEnsureDelegate ensureIndex,
            CustomIndexQueryPlannerDelegate queryPlanner,
            CustomIndexRebuildDelegate rebuildStrategy = null,
            IReadOnlyCollection<byte> requiredBsonTypes = null,
            IReadOnlyCollection<string> requiredPageTypes = null)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException("Plugin identifier must be provided.", nameof(pluginId));
            }

            if (string.IsNullOrWhiteSpace(strategyId))
            {
                throw new ArgumentException("Strategy identifier must be provided.", nameof(strategyId));
            }

            PluginId = pluginId;
            StrategyId = strategyId;
            EnsureIndex = ensureIndex ?? throw new ArgumentNullException(nameof(ensureIndex));
            QueryPlanner = queryPlanner ?? throw new ArgumentNullException(nameof(queryPlanner));
            RebuildStrategy = rebuildStrategy;
            RequiredBsonTypes = new ReadOnlyCollection<byte>(
                requiredBsonTypes != null ? new List<byte>(requiredBsonTypes) : Array.Empty<byte>());
            RequiredPageTypes = new ReadOnlyCollection<string>(
                requiredPageTypes != null ? new List<string>(requiredPageTypes) : Array.Empty<string>());
        }

        /// <summary>
        /// Gets the identifier of the plugin that owns the strategy.
        /// </summary>
        public string PluginId { get; }

        /// <summary>
        /// Gets the logical identifier exposed to public APIs.
        /// </summary>
        public string StrategyId { get; }

        /// <summary>
        /// Gets the delegate responsible for ensuring the index.
        /// </summary>
        public CustomIndexEnsureDelegate EnsureIndex { get; }

        /// <summary>
        /// Gets the delegate responsible for contributing to query planning.
        /// </summary>
        public CustomIndexQueryPlannerDelegate QueryPlanner { get; }

        /// <summary>
        /// Gets the optional delegate invoked during rebuild flows.
        /// </summary>
        public CustomIndexRebuildDelegate RebuildStrategy { get; }

        /// <summary>
        /// Gets the BSON type codes required by the strategy.
        /// </summary>
        public IReadOnlyCollection<byte> RequiredBsonTypes { get; }

        /// <summary>
        /// Gets the logical page types the strategy depends on.
        /// </summary>
        public IReadOnlyCollection<string> RequiredPageTypes { get; }
    }

    /// <summary>
    /// Represents the ensure-index operation passed to custom strategies.
    /// </summary>
    public sealed class CustomIndexEnsureContext
    {
        public CustomIndexEnsureContext(EnsureIndexContext ensureContext, BsonDocument options)
        {
            EnsureContext = ensureContext ?? throw new ArgumentNullException(nameof(ensureContext));
            Options = options ?? new BsonDocument();
        }

        /// <summary>
        /// Gets the base ensure-index context describing the request.
        /// </summary>
        public EnsureIndexContext EnsureContext { get; }

        /// <summary>
        /// Gets the custom index options supplied by the caller.
        /// </summary>
        public BsonDocument Options { get; }
    }

    /// <summary>
    /// Encapsulates data required for custom query planning.
    /// </summary>
    public sealed class CustomIndexQueryPlannerContext
    {
        public CustomIndexQueryPlannerContext(QueryPlanningContext planningContext)
        {
            PlanningContext = planningContext ?? throw new ArgumentNullException(nameof(planningContext));
        }

        /// <summary>
        /// Gets the underlying planning context.
        /// </summary>
        public QueryPlanningContext PlanningContext { get; }
    }

    /// <summary>
    /// Represents rebuild orchestration data exposed to custom strategies.
    /// </summary>
    public sealed class CustomIndexRebuildContext
    {
        public CustomIndexRebuildContext(LiteEngine engine, ILitePluginContext pluginContext)
        {
            Engine = engine ?? throw new ArgumentNullException(nameof(engine));
            PluginContext = pluginContext;
        }

        /// <summary>
        /// Gets the engine coordinating the rebuild.
        /// </summary>
        public LiteEngine Engine { get; }

        /// <summary>
        /// Gets the plugin context associated with the rebuild cycle.
        /// </summary>
        public ILitePluginContext PluginContext { get; }
    }
}
