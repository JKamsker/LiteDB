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

        private const string LegacyVectorPluginId = "LiteDB.Vector";
        private const string VectorFieldKey = "VectorField";
        private const string VectorTargetKey = "TargetEmbedding";
        private const string VectorMaxDistanceKey = "VectorMaxDistance";
        private const string VectorMetricKey = "VectorMetric";

        private static readonly string[] LegacyVectorReservedKeys = new[]
        {
            VectorFieldKey,
            VectorTargetKey,
            VectorMaxDistanceKey,
            VectorMetricKey
        };

        private const int VectorMetadataVersionNormalized = 2;
        private const byte DotProductMetric = 2;

        public BsonExpression Select { get; set; } = BsonExpression.Root;

        public List<BsonExpression> Includes { get; } = new List<BsonExpression>();
        public List<BsonExpression> Where { get; } = new List<BsonExpression>();

        public List<QueryOrder> OrderBy { get; } = new List<QueryOrder>();

        public BsonExpression GroupBy { get; set; } = null;
        public BsonExpression Having { get; set; } = null;

        public int Offset { get; set; } = 0;
        public int Limit { get; set; } = int.MaxValue;
        public bool ForUpdate { get; set; } = false;

        public bool HasVectorFilter
        {
            get
            {
                if (!TryGetMetadata(LegacyVectorPluginId, out var bag))
                {
                    return false;
                }

                if (!bag.TryGet<string>(VectorFieldKey, out var field) || string.IsNullOrWhiteSpace(field))
                {
                    return false;
                }

                return bag.TryGet<float[]>(VectorTargetKey, out var target) && target != null;
            }
        }

        [Obsolete("VectorField is provided via LiteDB.Vector metadata bag. Use QueryMetadataBag instead.")]
        public string VectorField
        {
            get
            {
                if (TryGetMetadata(LegacyVectorPluginId, out var bag))
                {
                    UpgradeVectorMetadataBag(bag);

                    if (bag.TryGet<string>(VectorFieldKey, out var field))
                    {
                        return field;
                    }
                }

                return null;
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    RemoveVectorMetadata(VectorFieldKey);
                    return;
                }

                EnsureVectorMetadataBag().Set(VectorFieldKey, value);
            }
        }

        [Obsolete("VectorTarget is provided via LiteDB.Vector metadata bag. Use QueryMetadataBag instead.")]
        public float[] VectorTarget
        {
            get
            {
                if (TryGetMetadata(LegacyVectorPluginId, out var bag))
                {
                    UpgradeVectorMetadataBag(bag);

                    if (bag.TryGet<float[]>(VectorTargetKey, out var target))
                    {
                        return target;
                    }
                }

                return null;
            }
            set
            {
                if (value == null)
                {
                    RemoveVectorMetadata(VectorTargetKey);
                    return;
                }

                EnsureVectorMetadataBag().Set(VectorTargetKey, value);
            }
        }

        [Obsolete("VectorMaxDistance is provided via LiteDB.Vector metadata bag. Use QueryMetadataBag instead.")]
        public double VectorMaxDistance
        {
            get
            {
                if (TryGetMetadata(LegacyVectorPluginId, out var bag))
                {
                    UpgradeVectorMetadataBag(bag);

                    if (bag.TryGet<double>(VectorMaxDistanceKey, out var distance))
                    {
                        return distance;
                    }
                }

                return double.MaxValue;
            }
            set
            {
                if (double.IsPositiveInfinity(value) || value == double.MaxValue)
                {
                    RemoveVectorMetadata(VectorMaxDistanceKey);
                    return;
                }

                var bag = EnsureVectorMetadataBag();
                var metric = GetVectorMetric(bag);
                var normalized = NormalizeVectorMaxDistance(value, metric);
                bag.Set(VectorMaxDistanceKey, normalized);
            }
        }

        [Obsolete("VectorMetric is provided via LiteDB.Vector metadata bag. Use QueryMetadataBag instead.")]
        public byte? VectorMetric
        {
            get
            {
                if (TryGetMetadata(LegacyVectorPluginId, out var bag))
                {
                    UpgradeVectorMetadataBag(bag);

                    if (bag.TryGet<byte?>(VectorMetricKey, out var metric))
                    {
                        return metric;
                    }
                }

                return null;
            }
            set
            {
                if (!value.HasValue)
                {
                    RemoveVectorMetadata(VectorMetricKey);
                    return;
                }

                var bag = EnsureVectorMetadataBag();
                bag.Set(VectorMetricKey, value);
                NormalizeStoredVectorMaxDistance(bag);
            }
        }

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

            if (TryGetVectorMetadata(out var vectorField, out var vectorTarget, out var vectorMaxDistance, out var vectorMetric))
            {
                var normalizedField = NormalizeVectorField(vectorField);
                var metricSegment = vectorMetric.HasValue ? $", {vectorMetric.Value}" : string.Empty;

                var vectorExpr = $"VECTOR_DIST({normalizedField}, [{string.Join(",", vectorTarget)}]{metricSegment})";
                if (this.Where.Count > 0)
                {
                    sb.AppendLine($"WHERE ({string.Join(" AND ", this.Where.Select(x => x.Source))}) AND {vectorExpr} <= {vectorMaxDistance}");
                }
                else
                {
                    sb.AppendLine($"WHERE {vectorExpr} <= {vectorMaxDistance}");
                }
            }
            else if (this.Where.Count > 0)
            {
                sb.AppendLine($"WHERE {string.Join(" AND ", this.Where.Select(x => x.Source))}");
            }

            return sb.ToString().Trim();
        }

        private QueryMetadataBag EnsureVectorMetadataBag()
        {
            if (_metadata.TryGetValue(LegacyVectorPluginId, out var existing))
            {
                UpgradeVectorMetadataBag(existing);
                return existing;
            }

            var bag = new QueryMetadataBag(LegacyVectorPluginId, version: VectorMetadataVersionNormalized, reservedKeys: LegacyVectorReservedKeys);
            _metadata[LegacyVectorPluginId] = bag;
            return bag;
        }

        private void RemoveVectorMetadata(string key)
        {
            if (!_metadata.TryGetValue(LegacyVectorPluginId, out var bag))
            {
                return;
            }

            if (bag.Remove(key) && bag.IsEmpty)
            {
                _metadata.Remove(LegacyVectorPluginId);
            }
        }

        private bool TryGetVectorMetadata(out string field, out float[] target, out double maxDistance, out byte? metric)
        {
            field = null;
            target = null;
            maxDistance = double.MaxValue;
            metric = null;

            if (!TryGetMetadata(LegacyVectorPluginId, out var bag))
            {
                return false;
            }

            UpgradeVectorMetadataBag(bag);

            if (!bag.TryGet<string>(VectorFieldKey, out field) || string.IsNullOrWhiteSpace(field))
            {
                return false;
            }

            if (!bag.TryGet<float[]>(VectorTargetKey, out target) || target == null)
            {
                return false;
            }

            if (bag.TryGet<double>(VectorMaxDistanceKey, out var distance))
            {
                maxDistance = distance;
            }

            if (bag.TryGet<byte?>(VectorMetricKey, out var vectorMetric))
            {
                metric = vectorMetric;
            }

            return true;
        }

        private void UpgradeVectorMetadataBag(QueryMetadataBag bag)
        {
            if (bag == null)
            {
                return;
            }

            if (bag.Version >= VectorMetadataVersionNormalized)
            {
                return;
            }

            NormalizeStoredVectorMaxDistance(bag);
            bag.SetVersion(VectorMetadataVersionNormalized);
        }

        private void NormalizeStoredVectorMaxDistance(QueryMetadataBag bag)
        {
            if (bag == null)
            {
                return;
            }

            if (!bag.TryGet<double>(VectorMaxDistanceKey, out var stored))
            {
                return;
            }

            var metric = GetVectorMetric(bag);
            var normalized = NormalizeVectorMaxDistance(stored, metric);

            if (normalized != stored)
            {
                bag.Set(VectorMaxDistanceKey, normalized);
            }
        }

        private static byte? GetVectorMetric(QueryMetadataBag bag)
        {
            if (bag != null && bag.TryGet<byte?>(VectorMetricKey, out var metric))
            {
                return metric;
            }

            return null;
        }

        private static double NormalizeVectorMaxDistance(double maxDistance, byte? metric)
        {
            if (!metric.HasValue || metric.Value != DotProductMetric)
            {
                return maxDistance;
            }

            if (double.IsNaN(maxDistance) || double.IsInfinity(maxDistance) || maxDistance >= double.MaxValue)
            {
                return maxDistance;
            }

            if (maxDistance > 0d)
            {
                return -maxDistance;
            }

            return maxDistance;
        }

        private static string NormalizeVectorField(string field)
        {
            if (string.IsNullOrEmpty(field))
            {
                return field;
            }

            field = field.Trim();

            if (field.StartsWith("$", StringComparison.Ordinal))
            {
                return field;
            }

            if (field.StartsWith(".", StringComparison.Ordinal))
            {
                return "$" + field;
            }

            return "$." + field;
        }
    }
}
