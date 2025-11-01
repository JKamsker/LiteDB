using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LiteDB.Engine;
using EngineIndex = LiteDB.Engine.Index;

namespace LiteDB.Plugins
{
    /// <summary>
    /// Provides the structured context passed to <see cref="IQueryPlanningRule"/> implementations.
    /// </summary>
    public sealed class QueryPlanningContext
    {
        private readonly List<BsonExpression> _consumedTerms = new List<BsonExpression>();
        private readonly List<BsonExpression> _additionalFilters = new List<BsonExpression>();

        internal QueryPlanningContext(
            Snapshot snapshot,
            Query query,
            IReadOnlyList<BsonExpression> terms,
            QueryPlan plan,
            ILitePluginContext pluginContext)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Query = query ?? throw new ArgumentNullException(nameof(query));
            Terms = terms ?? throw new ArgumentNullException(nameof(terms));
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            PluginContext = pluginContext;
        }

        internal Snapshot Snapshot { get; }

        /// <summary>
        /// Gets the query definition being optimized.
        /// </summary>
        public Query Query { get; }

        internal IReadOnlyList<BsonExpression> Terms { get; }

        internal QueryPlan Plan { get; }

        internal ILitePluginContext PluginContext { get; }

        /// <summary>
        /// Gets the ambient service provider exposed by the database, if any.
        /// </summary>
        public IServiceProvider Services => PluginContext?.Services;

        internal bool HasRewrite => _selectedIndex != null;

        internal EngineIndex SelectedIndex => _selectedIndex;

        internal string SelectedIndexExpression => _selectedIndexExpression;

        internal uint? SelectedIndexCost => _selectedIndexCost;

        internal bool SelectedIsIndexKeyOnly => _selectedIsIndexKeyOnly;

        internal IReadOnlyList<BsonExpression> ConsumedTerms => new ReadOnlyCollection<BsonExpression>(_consumedTerms);

        internal IReadOnlyList<BsonExpression> AdditionalFilters => new ReadOnlyCollection<BsonExpression>(_additionalFilters);

        internal bool ReplaceFilters => _replaceFilters;

        private EngineIndex _selectedIndex;
        private string _selectedIndexExpression;
        private uint? _selectedIndexCost;
        private bool _selectedIsIndexKeyOnly;
        private bool _replaceFilters;

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
            bool replaceFilters = false)
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
        }
    }
}
