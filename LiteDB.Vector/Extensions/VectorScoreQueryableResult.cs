using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using LiteDB.Vector.Engine;
using LiteDB.Vector.Query;

namespace LiteDB.Vector
{
    /// <summary>
    /// Wraps an <see cref="ILiteQueryableResult{T}"/> to project vector distance or similarity scores without recomputing query semantics.
    /// </summary>
    internal sealed class VectorScoreQueryableResult<T> : ILiteQueryableResult<VectorMatch<T>>
    {
        private readonly ILiteQueryableResult<T> _source;
        private readonly VectorScoreContext<T> _context;
        private readonly VectorScoreKind _kind;

        private VectorScoreQueryableResult(ILiteQueryableResult<T> source, VectorScoreContext<T> context, VectorScoreKind kind)
        {
            _source = source;
            _context = context;
            _kind = kind;
        }

        public static ILiteQueryableResult<VectorMatch<T>> Create(ILiteQueryableResult<T> source, VectorScoreKind kind)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var context = VectorScoreContext<T>.Create(source);
            return new VectorScoreQueryableResult<T>(source, context, kind);
        }

        public ILiteQueryableResult<VectorMatch<T>> Limit(int limit)
        {
            var limited = _source.Limit(limit);
            return new VectorScoreQueryableResult<T>(limited, VectorScoreContext<T>.Create(limited), _kind);
        }

        public ILiteQueryableResult<VectorMatch<T>> Skip(int offset)
        {
            var skipped = _source.Skip(offset);
            return new VectorScoreQueryableResult<T>(skipped, VectorScoreContext<T>.Create(skipped), _kind);
        }

        public ILiteQueryableResult<VectorMatch<T>> Offset(int offset)
        {
            var offseted = _source.Offset(offset);
            return new VectorScoreQueryableResult<T>(offseted, VectorScoreContext<T>.Create(offseted), _kind);
        }

        public ILiteQueryableResult<VectorMatch<T>> ForUpdate()
        {
            var updated = _source.ForUpdate();
            return new VectorScoreQueryableResult<T>(updated, VectorScoreContext<T>.Create(updated), _kind);
        }

        public BsonDocument GetPlan() => _source.GetPlan();

        public IBsonDataReader ExecuteReader()
        {
            throw new NotSupportedException("Vector score projections do not support materializing an IBsonDataReader. Call ToEnumerable() or ToDocuments() instead.");
        }

        public IEnumerable<BsonDocument> ToDocuments()
        {
            var mapper = BsonMapper.Global;
            return this.ToEnumerable()
                .Select(match => mapper.ToDocument(match));
        }

        public IEnumerable<VectorMatch<T>> ToEnumerable()
        {
            return this.EnumerateMatches();
        }

        public List<VectorMatch<T>> ToList()
        {
            return this.EnumerateMatches().ToList();
        }

        public VectorMatch<T>[] ToArray()
        {
            return this.EnumerateMatches().ToArray();
        }

        public int Into(string newCollection, BsonAutoId autoId = BsonAutoId.ObjectId)
        {
            throw new NotSupportedException("Vector score projections cannot be materialized into a collection using Into(). Project to documents first.");
        }

        public VectorMatch<T> First()
        {
            return this.EnumerateMatches().First();
        }

        public VectorMatch<T> FirstOrDefault()
        {
            return this.EnumerateMatches().FirstOrDefault();
        }

        public VectorMatch<T> Single()
        {
            return this.EnumerateMatches().Single();
        }

        public VectorMatch<T> SingleOrDefault()
        {
            return this.EnumerateMatches().SingleOrDefault();
        }

        public int Count()
        {
            return this.EnumerateMatches().Count();
        }

        public long LongCount()
        {
            return this.EnumerateMatches().LongCount();
        }

        public bool Exists()
        {
            return this.EnumerateMatches().Any();
        }

        private IEnumerable<VectorMatch<T>> EnumerateMatches()
        {
            var buffer = new List<(VectorMatch<T> Match, BsonValue Id)>();

            foreach (var item in _source.ToEnumerable())
            {
                if (!VectorScoreFactory.TryCreateMatch(item, _context, _kind, out var match, out var id))
                {
                    continue;
                }

                if (!_context.ShouldInclude(match))
                {
                    continue;
                }

                buffer.Add((match, id));
            }

            IEnumerable<(VectorMatch<T> Match, BsonValue Id)> ordered;

            if (_kind == VectorScoreKind.Similarity && _context.SupportsSimilarity)
            {
                ordered = buffer
                    .OrderByDescending(x => x.Match.Similarity ?? double.MinValue)
                    .ThenBy(x => x.Id);
            }
            else
            {
                ordered = buffer
                    .OrderBy(x => x.Match.Distance)
                    .ThenBy(x => x.Id);
            }

            foreach (var entry in ordered)
            {
                yield return entry.Match;
            }
        }

        private static class VectorScoreFactory
        {
            public static bool TryCreateMatch(T item, VectorScoreContext<T> context, VectorScoreKind kind, out VectorMatch<T> match, out BsonValue id)
            {
                match = default;
                id = BsonValue.Null;

                var document = context.Mapper.ToDocument(typeof(T), item);
                id = document["_id"];
                var value = context.VectorFieldExpression.ExecuteScalar(document, Collation.Default);

                if (!VectorExpressions.TryExtractVector(value, out var candidate))
                {
                    return false;
                }

                if (candidate.Length != context.Target.Length)
                {
                    return false;
                }

                var distance = VectorIndexService.ComputeDistance(candidate, context.Target, context.Metric, out var similarity);

                if (double.IsNaN(distance))
                {
                    return false;
                }

                var similarityValue = double.IsNaN(similarity) ? (double?)null : similarity;

                if (kind == VectorScoreKind.Similarity && !similarityValue.HasValue)
                {
                    throw VectorErrors.MetricDoesNotSupportSimilarity(context.Metric);
                }

                match = new VectorMatch<T>(item, distance, similarityValue);
                return true;
            }
        }

        private readonly struct VectorScoreContext<TDocument>
        {
            public VectorScoreContext(LiteQueryable<TDocument> queryable, BsonExpression vectorFieldExpression, float[] target, VectorDistanceMetric metric, double maxDistance)
            {
                Queryable = queryable;
                VectorFieldExpression = vectorFieldExpression;
                Target = target;
                Metric = metric;
                MaxDistance = maxDistance;
                Mapper = queryable.Mapper;
                HasDistanceFilter = !double.IsPositiveInfinity(maxDistance) && !double.IsNaN(maxDistance) && maxDistance < double.MaxValue;
            }

            public LiteQueryable<TDocument> Queryable { get; }

            public BsonExpression VectorFieldExpression { get; }

            public float[] Target { get; }

            public VectorDistanceMetric Metric { get; }

            public double MaxDistance { get; }

            public BsonMapper Mapper { get; }

            public bool HasDistanceFilter { get; }

            public bool SupportsSimilarity => Metric == VectorDistanceMetric.Cosine || Metric == VectorDistanceMetric.DotProduct;

            public static VectorScoreContext<TDocument> Create(ILiteQueryableResult<TDocument> source)
            {
                var queryable = Unwrap(source);
                var query = queryable.GetQueryDefinition();

                if (!query.TryGetMetadata(VectorQueryMetadata.PluginId, out var metadata))
                {
                    throw new InvalidOperationException("Vector score projections require a preceding vector query operation.");
                }

                if (!metadata.TryGet<string>(VectorQueryMetadata.FieldKey, out var field) ||
                    string.IsNullOrWhiteSpace(field) ||
                    !metadata.TryGet<float[]>(VectorQueryMetadata.TargetKey, out var target) ||
                    target == null)
                {
                    throw new InvalidOperationException("Vector score projections require a preceding vector query operation.");
                }

                var metricValue = VectorDistanceMetric.Cosine;

                if (metadata.TryGet<byte?>(VectorQueryMetadata.MetricKey, out var metricBytes) && metricBytes.HasValue)
                {
                    metricValue = (VectorDistanceMetric)metricBytes.Value;
                }

                var maxDistance = double.MaxValue;

                if (metadata.TryGet<double>(VectorQueryMetadata.MaxDistanceKey, out var storedDistance))
                {
                    maxDistance = storedDistance;
                }

                var fieldExpression = BsonExpression.Create(field, queryable.ExpressionRegistry);

                return new VectorScoreContext<TDocument>(queryable, fieldExpression, target, metricValue, maxDistance);
            }

            public bool ShouldInclude(VectorMatch<TDocument> match)
            {
                if (!HasDistanceFilter)
                {
                    return true;
                }

                if (Metric == VectorDistanceMetric.DotProduct)
                {
                    if (!match.Similarity.HasValue)
                    {
                        return false;
                    }

                    return match.Similarity.Value >= MaxDistance;
                }

                return match.Distance <= MaxDistance;
            }

            private static LiteQueryable<TDocument> Unwrap(ILiteQueryableResult<TDocument> source)
            {
                if (source is LiteQueryable<TDocument> queryable)
                {
                    return queryable;
                }

                throw new ArgumentException("Vector score projections require LiteQueryable results.", nameof(source));
            }
        }
    }
}
