using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LiteDB.Plugins.Query;
using static LiteDB.Constants;

namespace LiteDB
{
    /// <summary>
    /// Represent full query options
    /// </summary>
    public partial class Query
    {
        private readonly Dictionary<string, QueryMetadataBag> _metadata = new Dictionary<string, QueryMetadataBag>(StringComparer.Ordinal);


        public BsonExpression Select { get; set; } = BsonExpression.Root;

        public List<BsonExpression> Includes { get; } = new List<BsonExpression>();
        public List<BsonExpression> Where { get; } = new List<BsonExpression>();

        public List<QueryOrder> OrderBy { get; } = new List<QueryOrder>();

        public BsonExpression GroupBy { get; set; } = null;
        public BsonExpression Having { get; set; } = null;

        public int Offset { get; set; } = 0;
        public int Limit { get; set; } = int.MaxValue;
        public bool ForUpdate { get; set; } = false;


        public string Into { get; set; }
        public BsonAutoId IntoAutoId { get; set; } = BsonAutoId.ObjectId;

        public bool ExplainPlan { get; set; }

        public IEnumerable<string> RegisteredMetadata => _metadata.Keys;

        public void AttachMetadata(QueryMetadataBag bag)
        {
            if (bag == null)
            {
                throw new ArgumentNullException(nameof(bag));
            }

            _metadata[bag.PluginId] = bag;
        }

        public bool TryGetMetadata(string pluginId, out QueryMetadataBag bag)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                bag = null;
                return false;
            }

            return _metadata.TryGetValue(pluginId, out bag);
        }

        public QueryMetadataBag GetMetadata(string pluginId)
        {
            if (!TryGetMetadata(pluginId, out var bag))
            {
                throw new KeyNotFoundException($"No metadata bag has been attached for plugin '{pluginId}'.");
            }

            return bag;
        }

        public QueryMetadataBag GetOrCreateMetadata(string pluginId, Func<QueryMetadataBag> factory)
        {
            if (pluginId == null)
            {
                throw new ArgumentNullException(nameof(pluginId));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (_metadata.TryGetValue(pluginId, out var existing))
            {
                return existing;
            }

            var bag = factory();
            if (!string.Equals(bag?.PluginId, pluginId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Metadata bag plugin id '{bag?.PluginId}' does not match requested plugin id '{pluginId}'.");
            }

            _metadata[pluginId] = bag;
            return bag;
        }

        public bool RemoveMetadata(string pluginId)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                return false;
            }

            return _metadata.Remove(pluginId);
        }

        /// <summary>
        /// [ EXPLAIN ]
        ///    SELECT {selectExpr}
        ///    [ INTO {newcollection|$function} [ : {autoId} ] ]
        ///    [ FROM {collection|$function} ]
        /// [ INCLUDE {pathExpr0} [, {pathExprN} ]
        ///   [ WHERE {filterExpr} ]
        ///   [ GROUP BY {groupByExpr} ]
        ///  [ HAVING {filterExpr} ]
        ///   [ ORDER BY {orderByExpr} [ ASC | DESC ] ]
        ///   [ LIMIT {number} ]
        ///  [ OFFSET {number} ]
        ///     [ FOR UPDATE ]
        /// </summary>
        public string ToSQL(string collection)
        {
            var sb = new StringBuilder();

            if (this.ExplainPlan)
            {
                sb.AppendLine("EXPLAIN");
            }

            sb.AppendLine($"SELECT {this.Select.Source}");

            if (this.Into != null)
            {
                sb.AppendLine($"INTO {this.Into}:{IntoAutoId.ToString().ToLower()}");
            }

            sb.AppendLine($"FROM {collection}");

            if (this.Includes.Count > 0)
            {
                sb.AppendLine($"INCLUDE {string.Join(", ", this.Includes.Select(x => x.Source))}");
            }

            

            if (this.GroupBy != null)
            {
                sb.AppendLine($"GROUP BY {this.GroupBy.Source}");
            }

            if (this.Having != null)
            {
                sb.AppendLine($"HAVING {this.Having.Source}");
            }

            if (this.OrderBy.Count > 0)
            {
                var orderBy = this.OrderBy
                    .Select(x => $"{x.Expression.Source} {(x.Order == Query.Ascending ? "ASC" : "DESC")}");

                sb.AppendLine($"ORDER BY {string.Join(", ", orderBy)}");
            }

            if (this.Limit != int.MaxValue)
            {
                sb.AppendLine($"LIMIT {this.Limit}");
            }

            if (this.Offset != 0)
            {
                sb.AppendLine($"OFFSET {this.Offset}");
            }

            if (this.ForUpdate)
            {
                sb.AppendLine($"FOR UPDATE");
            }

            if (this.Where.Count > 0)
            {
                sb.AppendLine($"WHERE {string.Join(" AND ", this.Where.Select(x => x.Source))}");
            }

            return sb.ToString().Trim();
        }
    }
}
