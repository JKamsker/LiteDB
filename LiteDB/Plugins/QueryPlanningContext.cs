using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Plugins.Query;
using EngineIndex = LiteDB.Engine.Index;
using LiteDbQuery = LiteDB.Query;

namespace LiteDB.Plugins
{
    /// <summary>
    /// Provides the structured context passed to <see cref="IQueryPlanningRule"/> implementations.
    /// </summary>
    public sealed class QueryPlanningContext
    {
        private readonly List<BsonExpression> _consumedTerms = new List<BsonExpression>();
        private readonly List<BsonExpression> _additionalFilters = new List<BsonExpression>();
        private readonly ILitePluginContext _pluginContext;

        internal QueryPlanningContext(
            Snapshot snapshot,
            LiteDbQuery query,
            IReadOnlyList<BsonExpression> terms,
            QueryPlan plan,
            ILitePluginContext pluginContext)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Query = query ?? throw new ArgumentNullException(nameof(query));
            Terms = terms ?? throw new ArgumentNullException(nameof(terms));
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _pluginContext = pluginContext;
        }

        internal Snapshot Snapshot { get; }

        /// <summary>
        /// Gets the query definition being optimized.
        /// </summary>
        public LiteDbQuery Query { get; }

        internal IReadOnlyList<BsonExpression> Terms { get; }

        internal QueryPlan Plan { get; }

        /// <summary>
        /// Gets the ambient service provider exposed by the database, if any.
        /// </summary>
        public IServiceProvider Services => _pluginContext?.Services;

        /// <summary>
        /// Gets the metadata accessor registered by plugins, if available.
        /// </summary>
        public IQueryMetadataAccessor QueryMetadata => _pluginContext?.QueryMetadata;

        internal bool HasRewrite => _selectedIndex != null;

        internal EngineIndex SelectedIndex => _selectedIndex;

        internal string SelectedIndexExpression => _selectedIndexExpression;

        internal uint? SelectedIndexCost => _selectedIndexCost;

        internal bool SelectedIsIndexKeyOnly => _selectedIsIndexKeyOnly;

        internal string SelectedPluginId => _selectedPluginId;

        internal string SelectedPluginIndexKind => _selectedPluginIndexKind;

        internal BsonDocument SelectedPluginMetadata => _selectedPluginMetadata;

        internal IReadOnlyList<BsonExpression> ConsumedTerms => new ReadOnlyCollection<BsonExpression>(_consumedTerms);

        internal IReadOnlyList<BsonExpression> AdditionalFilters => new ReadOnlyCollection<BsonExpression>(_additionalFilters);

        internal bool ReplaceFilters => _replaceFilters;

        /// <summary>
        /// Indicates whether the plugin satisfied the order-by clause while planning.
        /// </summary>
        public bool OrderByConsumed { get; set; }

        /// <summary>
        /// Attempts to retrieve an attached metadata bag for the supplied plugin.
        /// </summary>
        /// <param name="pluginId">Identifier of the plugin that owns the metadata.</param>
        /// <param name="bag">The metadata bag when present.</param>
        /// <returns>True when metadata is available.</returns>
        public bool TryGetMetadata(string pluginId, out QueryMetadataBag bag)
        {
            if (Query == null)
            {
                bag = null;
                return false;
            }

            return Query.TryGetMetadata(pluginId, out bag);
        }

        /// <summary>
        /// Gets the metadata bag for a plugin, creating one from the registered descriptor when necessary.
        /// </summary>
        /// <param name="pluginId">Identifier of the plugin that owns the metadata.</param>
        /// <param name="factory">Optional factory used to create the metadata bag.</param>
        /// <returns>The metadata bag associated with the plugin.</returns>
        public QueryMetadataBag GetOrCreateMetadata(string pluginId, Func<QueryMetadataBag> factory = null)
        {
            if (Query == null)
            {
                throw new InvalidOperationException("Query metadata is unavailable because the query context was not supplied.");
            }

            QueryMetadataBag DefaultFactory()
            {
                if (QueryMetadata != null && QueryMetadata.TryGetDescriptor(pluginId, out var descriptor))
                {
                    return new QueryMetadataBag(descriptor);
                }

                return new QueryMetadataBag(pluginId, version: 1, reservedKeys: Array.Empty<string>());
            }

            return Query.GetOrCreateMetadata(pluginId, factory ?? DefaultFactory);
        }

        private EngineIndex _selectedIndex;
        private string _selectedIndexExpression;
        private uint? _selectedIndexCost;
        private bool _selectedIsIndexKeyOnly;
        private bool _replaceFilters;
        private string _selectedPluginId;
        private string _selectedPluginIndexKind;
        private BsonDocument _selectedPluginMetadata;

        /// <summary>
        /// Records the index the rule wants to use and which terms were consumed while planning.
        /// </summary>
        /// <param name="index">The index implementation to execute.</param>
        /// <param name="indexExpression">The expression that identifies the index usage.</param>
        /// <param name="consumedTerms">Optional subset of <see cref="Terms"/> handled by this rule.</param>
        /// <param name="isIndexKeyOnly">Indicates whether the index satisfies the projection without additional lookups.</param>
        /// <param name="indexCost">Optional pre-computed index cost.</param>
        /// <param name="additionalFilters">Optional residual filters to append after index traversal.</param>
        /// <param name="replaceFilters">When true, replaces all automatically derived filters with <paramref name="additionalFilters"/>.</param>
        internal void UseIndex(
            EngineIndex index,
            string indexExpression,
            IEnumerable<BsonExpression> consumedTerms = null,
            bool isIndexKeyOnly = false,
            uint? indexCost = null,
            IEnumerable<BsonExpression> additionalFilters = null,
            bool replaceFilters = false,
            string pluginId = null,
            string pluginIndexKind = null,
            BsonDocument pluginMetadata = null)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (string.IsNullOrWhiteSpace(indexExpression)) throw new ArgumentNullException(nameof(indexExpression));

            _selectedIndex = index;
            _selectedIndexExpression = indexExpression;
            _selectedIndexCost = indexCost;
            _selectedIsIndexKeyOnly = isIndexKeyOnly;
            _replaceFilters = replaceFilters;

            _consumedTerms.Clear();
            if (consumedTerms != null)
            {
                _consumedTerms.AddRange(consumedTerms);
            }

            _additionalFilters.Clear();
            if (additionalFilters != null)
            {
                _additionalFilters.AddRange(additionalFilters);
            }

            _selectedPluginId = pluginId;
            _selectedPluginIndexKind = pluginIndexKind;
            _selectedPluginMetadata = pluginMetadata;
        }
    }
}
