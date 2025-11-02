using LiteDB.Engine;
using LiteDB.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using static LiteDB.Constants;

namespace LiteDB
{
    /// <summary>
    /// An IQueryable-like class to write fluent query in documents in collection.
    /// </summary>
    public class LiteQueryable<T> : ILiteQueryable<T>
    {
        protected readonly ILiteEngine _engine;
        protected readonly BsonMapper _mapper;
        protected readonly string _collection;
        protected readonly Query _query;
        private readonly IExpressionRegistry _expressions;
        private readonly ILinqResolverRegistry _linqResolvers;
        private readonly LiteDatabase _database;
        private const byte DotProductMetric = 2;

        // indicate that T type are simple and result are inside first document fields (query always return a BsonDocument)
        private readonly bool _isSimpleType = Reflection.IsSimpleType(typeof(T));

        internal LiteQueryable(ILiteEngine engine, BsonMapper mapper, string collection, Query query, IExpressionRegistry expressions, LiteDatabase database, ILinqResolverRegistry linqResolvers)
        {
            _engine = engine;
            _mapper = mapper;
            _collection = collection;
            _query = query;
            _expressions = expressions;
            _database = database;
            _linqResolvers = linqResolvers;
        }

        internal Query GetQueryDefinition() => _query;

        internal BsonMapper Mapper => _mapper;

        internal IExpressionRegistry ExpressionRegistry => _expressions;

        internal LiteDatabase Database => _database;

        #region Includes

        /// <summary>
        /// Load cross reference documents from path expression (DbRef reference)
        /// </summary>
        public ILiteQueryable<T> Include<K>(Expression<Func<T, K>> path)
        {
            _query.Includes.Add(this.ResolveExpression(path));
            return this;
        }

        /// <summary>
        /// Load cross reference documents from path expression (DbRef reference)
        /// </summary>
        public ILiteQueryable<T> Include(BsonExpression path)
        {
            _query.Includes.Add(path);
            return this;
        }

        /// <summary>
        /// Load cross reference documents from path expression (DbRef reference)
        /// </summary>
        public ILiteQueryable<T> Include(List<BsonExpression> paths)
        {
            _query.Includes.AddRange(paths);
            return this;
        }

        #endregion

        #region Where

        /// <summary>
        /// Filters a sequence of documents based on a predicate expression
        /// </summary>
        public ILiteQueryable<T> Where(BsonExpression predicate)
        {
            _query.Where.Add(predicate);
            return this;
        }

        /// <summary>
        /// Filters a sequence of documents based on a predicate expression
        /// </summary>
        public ILiteQueryable<T> Where(string predicate, BsonDocument parameters)
        {
            _query.Where.Add(BsonExpression.Create(predicate, parameters, _expressions));
            return this;
        }

        /// <summary>
        /// Filters a sequence of documents based on a predicate expression
        /// </summary>
        public ILiteQueryable<T> Where(string predicate, params BsonValue[] args)
        {
            _query.Where.Add(BsonExpression.Create(predicate, _expressions, args));
            return this;
        }

        /// <summary>
        /// Filters a sequence of documents based on a predicate expression
        /// </summary>
        public ILiteQueryable<T> Where(Expression<Func<T, bool>> predicate)
        {
            return this.Where(this.ResolveExpression(predicate));
        }

        #endregion

        #region OrderBy

        /// <summary>
        /// Sort the documents of resultset in ascending (or descending) order according to a key.
        /// </summary>
        public ILiteQueryable<T> OrderBy(BsonExpression keySelector, int order = Query.Ascending)
        {
            if (_query.OrderBy.Count > 0) throw new ArgumentException("Multiple OrderBy calls are not supported. Use ThenBy for additional sort keys.");

            _query.OrderBy.Add(new QueryOrder(keySelector, order));
            return this;
        }

        /// <summary>
        /// Sort the documents of resultset in ascending (or descending) order according to a key (support only one OrderBy)
        /// </summary>
        public ILiteQueryable<T> OrderBy<K>(Expression<Func<T, K>> keySelector, int order = Query.Ascending)
        {
            return this.OrderBy(this.ResolveExpression(keySelector), order);
        }

        /// <summary>
        /// Sort the documents of resultset in descending order according to a key.
        /// </summary>
        public ILiteQueryable<T> OrderByDescending(BsonExpression keySelector) => this.OrderBy(keySelector, Query.Descending);

        /// <summary>
        /// Sort the documents of resultset in descending order according to a key.
        /// </summary>
        public ILiteQueryable<T> OrderByDescending<K>(Expression<Func<T, K>> keySelector) => this.OrderBy(keySelector, Query.Descending);

        /// <summary>
        /// Appends an ascending sort expression that is applied when previous keys are equal.
        /// </summary>
        public ILiteQueryable<T> ThenBy(BsonExpression keySelector)
        {
            if (_query.OrderBy.Count == 0) return this.OrderBy(keySelector, Query.Ascending);

            _query.OrderBy.Add(new QueryOrder(keySelector, Query.Ascending));
            return this;
        }

        /// <summary>
        /// Appends an ascending sort expression that is applied when previous keys are equal.
        /// </summary>
        public ILiteQueryable<T> ThenBy<K>(Expression<Func<T, K>> keySelector)
        {
            return this.ThenBy(this.ResolveExpression(keySelector));
        }

        /// <summary>
        /// Appends a descending sort expression that is applied when previous keys are equal.
        /// </summary>
        public ILiteQueryable<T> ThenByDescending(BsonExpression keySelector)
        {
            if (_query.OrderBy.Count == 0) return this.OrderBy(keySelector, Query.Descending);

            _query.OrderBy.Add(new QueryOrder(keySelector, Query.Descending));
            return this;
        }

        /// <summary>
        /// Appends a descending sort expression that is applied when previous keys are equal.
        /// </summary>
        public ILiteQueryable<T> ThenByDescending<K>(Expression<Func<T, K>> keySelector)
        {
            return this.ThenByDescending(this.ResolveExpression(keySelector));
        }

        #endregion

        #region GroupBy

        /// <summary>
        /// Groups the documents of resultset according to a specified key selector expression (support only one GroupBy)
        /// </summary>
        public ILiteQueryable<IGrouping<K, T>> GroupBy<K>(Expression<Func<T, K>> keySelector)
        {
            var expression = this.ResolveExpression(keySelector);

            this.GroupBy(expression);

            _mapper.RegisterGroupingType<K, T>();

            return new LiteQueryable<IGrouping<K, T>>(_engine, _mapper, _collection, _query, _expressions, _database, _linqResolvers);
        }

        /// <summary>
        /// Groups the documents of resultset according to a specified key selector expression (support only one GroupBy)
        /// </summary>
        public ILiteQueryable<T> GroupBy(BsonExpression keySelector)
        {
            if (_query.GroupBy != null) throw new ArgumentException("GROUP BY already defined in this query");

            _query.GroupBy = keySelector;
            return this;
        }

        #endregion

        #region Having

        /// <summary>
        /// Filter documents after group by pipe according to predicate expression (requires GroupBy and support only one Having)
        /// </summary>
        public ILiteQueryable<T> Having(BsonExpression predicate)
        {
            if (_query.Having != null) throw new ArgumentException("HAVING already defined in this query");

            _query.Having = predicate;
            return this;
        }

        #endregion

        #region Select

        /// <summary>
        /// Transform input document into a new output document. Can be used with each document, group by or all source
        /// </summary>
        public ILiteQueryable<BsonDocument> Select(BsonExpression selector)
        {
            _query.Select = selector;

            return new LiteQueryable<BsonDocument>(_engine, _mapper, _collection, _query, _expressions, _database, _linqResolvers);
        }

        /// <summary>
        /// Project each document of resultset into a new document/value based on selector expression
        /// </summary>
        public ILiteQueryable<K> Select<K>(Expression<Func<T, K>> selector)
        {
            _query.Select = this.ResolveExpression(selector);

            return new LiteQueryable<K>(_engine, _mapper, _collection, _query, _expressions, _database, _linqResolvers);
        }

        private static void ValidateVectorArguments(float[] target, double maxDistance)
        {
            if (target == null || target.Length == 0) throw new ArgumentException("Target vector must be provided.", nameof(target));
            if (double.IsNaN(maxDistance)) throw new ArgumentOutOfRangeException(nameof(maxDistance), "Similarity threshold must be a valid number.");
        }

        private static bool ShouldInvertDotProductThreshold(double value)
        {
            return !double.IsNaN(value) &&
                !double.IsInfinity(value) &&
                value < double.MaxValue;
        }

        private BsonExpression CreateVectorDistanceFilter(BsonExpression fieldExpr, float[] target, double maxDistance, byte? metric)
        {
            if (fieldExpr == null) throw new ArgumentNullException(nameof(fieldExpr));

            ValidateVectorArguments(target, maxDistance);

            var comparisonThreshold = maxDistance;

            if (metric.HasValue &&
                metric.Value == DotProductMetric &&
                ShouldInvertDotProductThreshold(maxDistance))
            {
                comparisonThreshold = -maxDistance;
            }

            var parameters = new List<BsonValue>
            {
                new BsonArray(target.Select(v => new BsonValue(v))),
                new BsonValue(comparisonThreshold)
            };

            var metricPlaceholder = string.Empty;

            if (metric.HasValue)
            {
                parameters.Add(new BsonValue(metric.Value));
                metricPlaceholder = ", @2";
            }

            var vectorExpr = $"VECTOR_DIST({fieldExpr.Source}, @0{metricPlaceholder})";
            return BsonExpression.Create($"({vectorExpr}) <= @1", _expressions, parameters.ToArray());
        }

        private BsonExpression CreateVectorDistanceExpression(BsonExpression fieldExpr, float[] target, byte? metric)
        {
            if (fieldExpr == null) throw new ArgumentNullException(nameof(fieldExpr));

            ValidateVectorArguments(target, double.MaxValue);

            var parameters = new List<BsonValue>
            {
                new BsonArray(target.Select(v => new BsonValue(v)))
            };

            var metricPlaceholder = string.Empty;

            if (metric.HasValue)
            {
                parameters.Add(new BsonValue(metric.Value));
                metricPlaceholder = ", @1";
            }

            return BsonExpression.Create($"VECTOR_DIST({fieldExpr.Source}, @0{metricPlaceholder})", _expressions, parameters.ToArray());
        }

        private void ConfigureVectorContext(BsonExpression fieldExpr, float[] target, double maxDistance, byte? metric)
        {
            _query.VectorField = fieldExpr.Source;
            _query.VectorTarget = target?.ToArray();
            _query.VectorMaxDistance = maxDistance;
            _query.VectorMetric = metric;
        }

        internal ILiteQueryable<T> VectorWhereNear(string vectorField, float[] target, double maxDistance, byte? metric = null)
        {
            if (string.IsNullOrWhiteSpace(vectorField)) throw new ArgumentNullException(nameof(vectorField));

            var fieldExpr = BsonExpression.Create($"$.{vectorField}", _expressions);
            return this.VectorWhereNear(fieldExpr, target, maxDistance, metric);
        }

        internal ILiteQueryable<T> VectorWhereNear(BsonExpression fieldExpr, float[] target, double maxDistance, byte? metric = null)
        {
            var effectiveMetric = metric ?? _query.VectorMetric;

            var filter = CreateVectorDistanceFilter(fieldExpr, target, maxDistance, effectiveMetric);

            _query.Where.Add(filter);

            this.ConfigureVectorContext(fieldExpr, target, maxDistance, effectiveMetric);

            return this;
        }

        internal ILiteQueryable<T> VectorWhereNear<K>(Expression<Func<T, K>> field, float[] target, double maxDistance, byte? metric = null)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));

            var fieldExpr = this.ResolveExpression(field);
            return this.VectorWhereNear(fieldExpr, target, maxDistance, metric);
        }

        internal ILiteQueryableResult<T> VectorTopKNear<K>(Expression<Func<T, K>> field, float[] target, int k, byte? metric = null, double? maxDistance = null)
        {
            var fieldExpr = this.ResolveExpression(field);
            return this.VectorTopKNear(fieldExpr, target, k, metric, maxDistance);
        }

        internal ILiteQueryableResult<T> VectorTopKNear(string field, float[] target, int k, byte? metric = null, double? maxDistance = null)
        {
            var fieldExpr = BsonExpression.Create($"$.{field}", _expressions);
            return this.VectorTopKNear(fieldExpr, target, k, metric, maxDistance);
        }

        internal ILiteQueryableResult<T> VectorTopKNear(BsonExpression fieldExpr, float[] target, int k, byte? metric = null, double? maxDistance = null)
        {
            if (fieldExpr == null) throw new ArgumentNullException(nameof(fieldExpr));
            if (target == null || target.Length == 0) throw new ArgumentException("Target vector must be provided.", nameof(target));
            if (k <= 0) throw new ArgumentOutOfRangeException(nameof(k), "Top-K must be greater than zero.");

            var effectiveMaxDistance = maxDistance ?? double.MaxValue;

            if (maxDistance.HasValue)
            {
                this.VectorWhereNear(fieldExpr, target, effectiveMaxDistance, metric);
            }
            else
            {
                this.ConfigureVectorContext(fieldExpr, target, effectiveMaxDistance, metric);
            }

            var distExpr = this.CreateVectorDistanceExpression(fieldExpr, target, metric);

            return this
                .OrderBy(distExpr, Query.Ascending)
                .ThenBy(BsonExpression.Create("$._id", _expressions))
                .Limit(k);
        }

        internal ILiteQueryable<T> VectorOrderByNearest<K>(Expression<Func<T, K>> field, float[] target, byte? metric = null, double? maxDistance = null)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));

            var fieldExpr = this.ResolveExpression(field);
            return this.VectorOrderByNearest(fieldExpr, target, metric, maxDistance);
        }

        internal ILiteQueryable<T> VectorOrderByNearest(string field, float[] target, byte? metric = null, double? maxDistance = null)
        {
            var fieldExpr = BsonExpression.Create($"$.{field}", _expressions);
            return this.VectorOrderByNearest(fieldExpr, target, metric, maxDistance);
        }

        internal ILiteQueryable<T> VectorOrderByNearest(BsonExpression fieldExpr, float[] target, byte? metric = null, double? maxDistance = null)
        {
            if (fieldExpr == null) throw new ArgumentNullException(nameof(fieldExpr));

            var effectiveMaxDistance = maxDistance ?? double.MaxValue;

            if (maxDistance.HasValue)
            {
                this.VectorWhereNear(fieldExpr, target, effectiveMaxDistance, metric);
            }
            else
            {
                this.ConfigureVectorContext(fieldExpr, target, effectiveMaxDistance, metric);
            }

            var distExpr = this.CreateVectorDistanceExpression(fieldExpr, target, metric);

            return this
                .OrderBy(distExpr, Query.Ascending)
                .ThenBy(BsonExpression.Create("$._id", _expressions));
        }

        [Obsolete("Add `using LiteDB.Vector;` and call the LiteQueryableVectorExtensions.WhereNear extension instead.")]
        public ILiteQueryable<T> WhereNear(string vectorField, float[] target, double maxDistance)
        {
            return this.VectorWhereNear(vectorField, target, maxDistance);
        }

        [Obsolete("Add `using LiteDB.Vector;` and call the LiteQueryableVectorExtensions.WhereNear extension instead.")]
        public ILiteQueryable<T> WhereNear(BsonExpression fieldExpr, float[] target, double maxDistance)
        {
            return this.VectorWhereNear(fieldExpr, target, maxDistance);
        }

        [Obsolete("Add `using LiteDB.Vector;` and call the LiteQueryableVectorExtensions.WhereNear extension instead.")]
        public ILiteQueryable<T> WhereNear<K>(Expression<Func<T, K>> field, float[] target, double maxDistance)
        {
            return this.VectorWhereNear(field, target, maxDistance);
        }

        [Obsolete("Add `using LiteDB.Vector;` and call the LiteQueryableVectorExtensions.FindNearest extension instead.")]
        public IEnumerable<T> FindNearest(string vectorField, float[] target, double maxDistance)
        {
            return this.VectorWhereNear(vectorField, target, maxDistance).ToEnumerable();
        }

        [Obsolete("Add `using LiteDB.Vector;` and call the LiteQueryableVectorExtensions.TopKNear extension instead.")]
        public ILiteQueryableResult<T> TopKNear<K>(Expression<Func<T, K>> field, float[] target, int k)
        {
            return this.VectorTopKNear(field, target, k);
        }

        [Obsolete("Add `using LiteDB.Vector;` and call the LiteQueryableVectorExtensions.TopKNear extension instead.")]
        public ILiteQueryableResult<T> TopKNear(string field, float[] target, int k)
        {
            return this.VectorTopKNear(field, target, k);
        }

        [Obsolete("Add `using LiteDB.Vector;` and call the LiteQueryableVectorExtensions.TopKNear extension instead.")]
        public ILiteQueryableResult<T> TopKNear(BsonExpression fieldExpr, float[] target, int k)
        {
            return this.VectorTopKNear(fieldExpr, target, k);
        }

        #endregion

        #region Offset/Limit/ForUpdate

        /// <summary>
        /// Execute query locking collection in write mode. This is avoid any other thread change results after read document and before transaction ends
        /// </summary>
        public ILiteQueryableResult<T> ForUpdate()
        {
            _query.ForUpdate = true;
            return this;
        }

        /// <summary>
        /// Bypasses a specified number of documents in resultset and retun the remaining documents (same as Skip)
        /// </summary>
        public ILiteQueryableResult<T> Offset(int offset)
        {
            _query.Offset = offset;
            return this;
        }

        /// <summary>
        /// Bypasses a specified number of documents in resultset and retun the remaining documents (same as Offset)
        /// </summary>
        public ILiteQueryableResult<T> Skip(int offset) => this.Offset(offset);

        /// <summary>
        /// Return a specified number of contiguous documents from start of resultset
        /// </summary>
        public ILiteQueryableResult<T> Limit(int limit)
        {
            _query.Limit = limit;
            return this;
        }

        #endregion

        #region Execute Result

        /// <summary>
        /// Execute query and returns resultset as generic BsonDataReader
        /// </summary>
        public IBsonDataReader ExecuteReader()
        {
            _query.ExplainPlan = false;

            return _engine.Query(_collection, _query);
        }

        /// <summary>
        /// Execute query and return resultset as IEnumerable of documents
        /// </summary>
        public IEnumerable<BsonDocument> ToDocuments()
        {
            using (var reader = this.ExecuteReader())
            {
                while (reader.Read())
                {
                    yield return reader.Current as BsonDocument;
                }
            }
        }

        /// <summary>
        /// Execute query and return resultset as IEnumerable of T. If T is a ValueType or String, return values only (not documents)
        /// </summary>
        public IEnumerable<T> ToEnumerable()
        {
            if (_isSimpleType)
            {
                return this.ToDocuments()
                    .Select(x => x[x.Keys.First()])
                    .Select(x => (T)_mapper.Deserialize(typeof(T), x));
            }
            else
            {
                return this.ToDocuments()
                    .Select(x => (T)_mapper.Deserialize(typeof(T), x));
            }
        }

        /// <summary>
        /// Execute query and return results as a List
        /// </summary>
        public List<T> ToList()
        {
            return this.ToEnumerable().ToList();
        }

        /// <summary>
        /// Execute query and return results as an Array
        /// </summary>
        public T[] ToArray()
        {
            return this.ToEnumerable().ToArray();
        }

        /// <summary>
        /// Get execution plan over current query definition to see how engine will execute query
        /// </summary>
        public BsonDocument GetPlan()
        {
            _query.ExplainPlan = true;

            var reader = _engine.Query(_collection, _query);

            return reader.ToEnumerable().FirstOrDefault()?.AsDocument;
        }

        #endregion

        #region Execute Single/First

        /// <summary>
        /// Returns the only document of resultset, and throw an exception if there not exactly one document in the sequence
        /// </summary>
        public T Single()
        {
            return this.ToEnumerable().Single();
        }

        /// <summary>
        /// Returns the only document of resultset, or null if resultset are empty; this method throw an exception if there not exactly one document in the sequence
        /// </summary>
        public T SingleOrDefault()
        {
            return this.ToEnumerable().SingleOrDefault();
        }

        /// <summary>
        /// Returns first document of resultset
        /// </summary>
        public T First()
        {
            return this.ToEnumerable().First();
        }

        /// <summary>
        /// Returns first document of resultset or null if resultset are empty
        /// </summary>
        public T FirstOrDefault()
        {
            return this.ToEnumerable().FirstOrDefault();
        }

        #endregion

        #region Execute Count

        /// <summary>
        /// Execute Count methos in filter query
        /// </summary>
        public int Count()
        {
            var oldSelect = _query.Select;

            try
            {
                this.Select($"{{ count: COUNT(*._id) }}");
                var ret = this.ToDocuments().Single()["count"].AsInt32;

                return ret;
            }
            finally
            {
                _query.Select = oldSelect;
            }
        }

        /// <summary>
        /// Execute Count methos in filter query
        /// </summary>
        public long LongCount()
        {
            var oldSelect = _query.Select;

            try
            {
                this.Select($"{{ count: COUNT(*._id) }}");
                var ret = this.ToDocuments().Single()["count"].AsInt64;

                return ret;
            }
            finally
            {
                _query.Select = oldSelect;
            }
        }

        /// <summary>
        /// Returns true/false if query returns any result
        /// </summary>
        public bool Exists()
        {
            var oldSelect = _query.Select;

            try
            {
                this.Select($"{{ exists: ANY(*._id) }}");
                var ret = this.ToDocuments().Single()["exists"].AsBoolean;

                return ret;
            }
            finally
            {
                _query.Select = oldSelect;
            }
        }

        #endregion

        #region Execute Into

        public int Into(string newCollection, BsonAutoId autoId = BsonAutoId.ObjectId)
        {
            _query.Into = newCollection;
            _query.IntoAutoId = autoId;

            using (var reader = this.ExecuteReader())
            {
                return reader.Current.AsInt32;
            }
        }

        #endregion

        private BsonExpression ResolveExpression<K>(Expression<Func<T, K>> expression)
        {
            return _mapper.GetExpression(expression, _expressions, _database, _linqResolvers);
        }
    }
}
