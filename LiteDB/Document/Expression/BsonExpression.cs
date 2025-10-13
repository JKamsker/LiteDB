using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using LiteDB.Plugins;
using static LiteDB.Constants;

namespace LiteDB
{
    /// <summary>
    /// Delegate function to get compiled enumerable expression
    /// </summary>
    internal delegate IEnumerable<BsonValue> BsonExpressionEnumerableDelegate(IEnumerable<BsonDocument> source, BsonDocument root, BsonValue current, Collation collation, BsonDocument parameters);

    /// <summary>
    /// Delegate function to get compiled scalar expression
    /// </summary>
    internal delegate BsonValue BsonExpressionScalarDelegate(IEnumerable<BsonDocument> source, BsonDocument root, BsonValue current, Collation collation, BsonDocument parameters);

    /// <summary>
    /// Compile and execute string expressions using BsonDocuments. Used in all document manipulation (transform, filter, indexes, updates). See https://github.com/mbdavid/LiteDB/wiki/Expressions
    /// </summary>
    public sealed class BsonExpression
    {
        /// <summary>
        /// Get formatted expression
        /// </summary>
        public string Source { get; internal set; }

        /// <summary>
        /// Indicate expression type
        /// </summary>
        public BsonExpressionType Type { get; internal set; }

        /// <summary>
        /// If true, this expression do not change if same document/paramter are passed (only few methods change - like NOW() - or parameters)
        /// </summary>
        public bool IsImmutable { get; internal set; }

        /// <summary>
        /// Get/Set parameter values that will be used on expression execution
        /// </summary>
        public BsonDocument Parameters { get; internal set; }

        /// <summary>
        /// In predicate expressions, indicate Left side
        /// </summary>
        internal BsonExpression Left { get; set; }

        /// <summary>
        /// In predicate expressions, indicate Rigth side
        /// </summary>
        internal BsonExpression Right { get; set; }

        /// <summary>
        /// Get/Set this expression (or any inner expression) use global Source (*)
        /// </summary>
        internal bool UseSource { get; set; }

        /// <summary>
        /// Get transformed LINQ expression
        /// </summary>
        internal Expression Expression { get; set; }

        /// <summary>
        /// Fill this hashset with all fields used in root level of document (be used to partial deserialize) - "$" means all fields
        /// </summary>
        public HashSet<string> Fields { get; internal set; }

        /// <summary>
        /// Indicate if this expressions returns a single value or IEnumerable value
        /// </summary>
        public bool IsScalar { get; internal set; }

        /// <summary>
        /// Indicate that expression evaluate to TRUE or FALSE (=, >, ...). OR and AND are not considered Predicate expressions
        /// Predicate expressions must have Left/Right expressions
        /// </summary>
        internal bool IsPredicate =>
            this.Type == BsonExpressionType.Equal ||
            this.Type == BsonExpressionType.Like ||
            this.Type == BsonExpressionType.Between ||
            this.Type == BsonExpressionType.GreaterThan ||
            this.Type == BsonExpressionType.GreaterThanOrEqual ||
            this.Type == BsonExpressionType.LessThan ||
            this.Type == BsonExpressionType.LessThanOrEqual ||
            this.Type == BsonExpressionType.NotEqual ||
            this.Type == BsonExpressionType.In;

        /// <summary>
        /// This expression can be indexed? To index some expression must contains fields (at least 1) and
        /// must use only immutable methods and no parameters
        /// </summary>
        internal bool IsIndexable =>
            this.Fields.Count > 0 &&
            this.IsImmutable == true &&
            this.Parameters.Count == 0;

        /// <summary>
        /// This expression has no dependency of BsonDocument so can be used as user value (when select index)
        /// </summary>
        internal bool IsValue =>
            this.Fields.Count == 0;

        /// <summary>
        /// Indicate when predicate expression uses ANY keywork for filter array items
        /// </summary>
        internal bool IsANY =>
            this.IsPredicate &&
            this.Expression.ToString().Contains("_ANY");

        /// <summary>
        /// Compiled Expression into a function to be executed: func(source[], root, current, parameters)[]
        /// </summary>
        private BsonExpressionEnumerableDelegate _funcEnumerable;

        /// <summary>
        /// Compiled Expression into a scalar function to be executed: func(source[], root, current, parameters)1
        /// </summary>
        private BsonExpressionScalarDelegate _funcScalar;

        private static readonly IExpressionRegistry _builtInRegistry = new ExpressionRegistry();
        private static Func<IExpressionRegistry> _defaultRegistryResolver = () => _builtInRegistry;

        internal static IExpressionRegistry ResolveRegistry(IExpressionRegistry registry)
        {
            if (registry != null)
            {
                return registry;
            }

            var resolved = _defaultRegistryResolver?.Invoke();

            if (resolved == null)
            {
                throw new LiteException(0, "No default expression registry is available. Use overloads that accept IExpressionRegistry explicitly.");
            }

            return resolved;
        }

        internal static IExpressionRegistry BuiltInRegistry => _builtInRegistry;

        internal static Func<IExpressionRegistry> DefaultRegistryResolver
        {
            get => _defaultRegistryResolver;
            set => _defaultRegistryResolver = value ?? (() => _builtInRegistry);
        }

        /// <summary>
        /// Get default field name when need convert simple BsonValue into BsonDocument
        /// </summary>
        internal string DefaultFieldName()
        {
            var name = string.Join("_", this.Fields.Where(x => x != "$"));

            return string.IsNullOrEmpty(name) ? "expr" : name;
        }

        /// <summary>
        /// Only internal ctor (from BsonParserExpression)
        /// </summary>
        internal BsonExpression()
        {
        }

        /// <summary>
        /// Implicit string converter
        /// </summary>
        public static implicit operator String(BsonExpression expr)
        {
            return expr.Source;
        }

        /// <summary>
        /// Implicit string converter
        /// </summary>
        public static implicit operator BsonExpression(String expr)
        {
            return BsonExpression.Create(expr);
        }

        #region Execute Enumerable

        /// <summary>
        /// Execute expression with an empty document (used only for resolve math/functions).
        /// </summary>
        public IEnumerable<BsonValue> Execute(Collation collation = null)
        {
            var root = new BsonDocument();
            var source = new BsonDocument[] { root };

            return this.Execute(source, root, root, collation);
        }

        /// <summary>
        /// Execute expression and returns IEnumerable values
        /// </summary>
        public IEnumerable<BsonValue> Execute(BsonDocument root, Collation collation = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            var source = new BsonDocument[] { root };

            return this.Execute(source, root, root, collation);
        }

        /// <summary>
        /// Execute expression and returns IEnumerable values
        /// </summary>
        public IEnumerable<BsonValue> Execute(IEnumerable<BsonDocument> source, Collation collation = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return this.Execute(source, null, null, collation);
        }

        /// <summary>
        /// Execute expression and returns IEnumerable values - returns NULL if no elements
        /// </summary>
        internal IEnumerable<BsonValue> Execute(IEnumerable<BsonDocument> source, BsonDocument root, BsonValue current, Collation collation)
        {
            if (this.IsScalar)
            {
                var value = _funcScalar(source, root, current, collation ?? Collation.Binary, this.Parameters);

                yield return value;
            }
            else
            {
                var values = _funcEnumerable(source, root, current, collation ?? Collation.Binary, this.Parameters);

                foreach (var value in values)
                {
                    yield return value;
                }
            }
        }

        /// <summary>
        /// Execute expression over document to get all index keys. 
        /// Return distinct value (no duplicate key to same document)
        /// </summary>
        internal IEnumerable<BsonValue> GetIndexKeys(BsonDocument doc, Collation collation)
        {
            return this.Execute(doc, collation).Distinct();
        }

        #endregion

        #region ExecuteScalar

        /// <summary>
        /// Execute scalar expression with an blank document and empty source (used only for resolve math/functions).
        /// </summary>
        public BsonValue ExecuteScalar(Collation collation = null)
        {
            var root = new BsonDocument();
            var source = new BsonDocument[] { };

            return this.ExecuteScalar(source, root, root, collation);
        }

        /// <summary>
        /// Execute scalar expression over single document and return a single value (or BsonNull when empty). Throws exception if expression are not scalar expression
        /// </summary>
        public BsonValue ExecuteScalar(BsonDocument root, Collation collation = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            var source = new BsonDocument[] { root };

            return this.ExecuteScalar(source, root, root, collation);
        }

        /// <summary>
        /// Execute scalar expression over multiple documents and return a single value (or BsonNull when empty). Throws exception if expression are not scalar expression
        /// </summary>
        public BsonValue ExecuteScalar(IEnumerable<BsonDocument> source, Collation collation = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return this.ExecuteScalar(source, null, null, collation);
        }

        /// <summary>
        /// Execute expression and returns IEnumerable values - returns NULL if no elements
        /// </summary>
        internal BsonValue ExecuteScalar(IEnumerable<BsonDocument> source, BsonDocument root, BsonValue current, Collation collation)
        {
            if (this.IsScalar)
            {
                return _funcScalar(source, root, current, collation ?? Collation.Binary, this.Parameters);
            }
            else
            {
                throw new LiteException(0, $"Expression `{this.Source}` is not a scalar expression and can return more than one result");
            }
        }

        #endregion

        #region Static method

        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            private readonly IExpressionRegistry _registry;
            private readonly string _source;

            public CacheKey(IExpressionRegistry registry, string source)
            {
                _registry = registry;
                _source = source;
            }

            public bool Equals(CacheKey other)
            {
                return ReferenceEquals(_registry, other._registry) && string.Equals(_source, other._source, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is CacheKey other && this.Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = _registry != null ? RuntimeHelpers.GetHashCode(_registry) : 0;
                    hash = (hash * 397) ^ (_source != null ? _source.GetHashCode() : 0);
                    return hash;
                }
            }
        }

        private static readonly ConcurrentDictionary<CacheKey, BsonExpressionEnumerableDelegate> _cacheEnumerable = new ConcurrentDictionary<CacheKey, BsonExpressionEnumerableDelegate>();
        private static readonly ConcurrentDictionary<CacheKey, BsonExpressionScalarDelegate> _cacheScalar = new ConcurrentDictionary<CacheKey, BsonExpressionScalarDelegate>();

        /// <summary>
        /// Parse string and create new instance of BsonExpression - can be cached
        /// </summary>
        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static BsonExpression Create(string expression)
        {
            return Create(expression, new BsonDocument(), null);
        }

        /// <summary>
        /// Parse string and create new instance of BsonExpression - can be cached
        /// </summary>
        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static BsonExpression Create(string expression, params BsonValue[] args)
        {
            return Create(expression, ResolveRegistry(null), args);
        }

        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static BsonExpression Create(BsonExpression expression, params BsonValue[] args)
        {
            return Create(expression, ResolveRegistry(null), args);
        }

        /// <summary>
        /// Parse string and create new instance of BsonExpression - can be cached
        /// </summary>
        [Obsolete("Use overloads that accept IExpressionRegistry explicitly.")]
        public static BsonExpression Create(string expression, BsonDocument parameters)
        {
            return Create(expression, parameters, null);
        }

        public static BsonExpression Create(string expression, IExpressionRegistry registry)
        {
            return Create(expression, new BsonDocument(), registry);
        }

        public static BsonExpression Create(string expression, BsonDocument parameters, IExpressionRegistry registry)
        {
            if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentNullException(nameof(expression));

            var resolvedRegistry = ResolveRegistry(registry);
            var tokenizer = new Tokenizer(expression, resolvedRegistry);

            var expr = Create(tokenizer, BsonExpressionParserMode.Full, parameters, resolvedRegistry);

            tokenizer.LookAhead().Expect(TokenType.EOF);

            return expr;
        }

        public static BsonExpression Create(string expression, IExpressionRegistry registry, params BsonValue[] args)
        {
            var parameters = new BsonDocument();

            for (var i = 0; i < args.Length; i++)
            {
                parameters[i.ToString()] = args[i];
            }

            return Create(expression, parameters, registry);
        }

        public static BsonExpression Create(BsonExpression expression, IExpressionRegistry registry, params BsonValue[] args)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));

            var parameters = new BsonDocument();

            for (var i = 0; i < args.Length; i++)
            {
                parameters[i.ToString()] = args[i];
            }

            return Create(expression.Source, parameters, registry);
        }

        public static BsonExpression Compile(string expression, IExpressionRegistry registry)
        {
            return Create(expression, registry);
        }

        /// <summary>
        /// Parse tokenizer and create new instance of BsonExpression - for now, do not use cache
        /// </summary>
        internal static BsonExpression Create(Tokenizer tokenizer, BsonExpressionParserMode mode, BsonDocument parameters, IExpressionRegistry registry)
        {
            if (tokenizer == null) throw new ArgumentNullException(nameof(tokenizer));

            var resolvedRegistry = ResolveRegistry(registry);

            return ParseAndCompile(tokenizer, mode, parameters, DocumentScope.Root, resolvedRegistry);
        }

        /// <summary>
        /// Parse and compile string expression and return BsonExpression
        /// </summary>
        internal static BsonExpression ParseAndCompile(Tokenizer tokenizer, BsonExpressionParserMode mode, BsonDocument parameters, DocumentScope scope, IExpressionRegistry registry)
        {
            if (tokenizer == null) throw new ArgumentNullException(nameof(tokenizer));

            var context = new ExpressionContext();

            var parser = new BsonExpressionParser(registry);

            var expr =
                mode == BsonExpressionParserMode.Full ? parser.ParseFullExpression(tokenizer, context, parameters, scope) :
                mode == BsonExpressionParserMode.Single ? parser.ParseSingleExpression(tokenizer, context, parameters, scope) :
                mode == BsonExpressionParserMode.SelectDocument ? parser.ParseSelectDocumentBuilder(tokenizer, context, parameters) :
                parser.ParseUpdateDocumentBuilder(tokenizer, context, parameters);

            // compile linq expression (with left+right expressions)
            Compile(expr, context, registry);

            return expr;
        }

        internal static void Compile(BsonExpression expr, ExpressionContext context, IExpressionRegistry registry)
        {
            // compile linq expression according with return type (scalar or enumerable)
            // in both case, try use cached compiled version
            var cacheKey = new CacheKey(registry, expr.Source);

            var body = NormalizeParameters(expr.Expression, context);

            if (expr.IsScalar)
            {
                var cached = _cacheScalar.GetOrAdd(cacheKey, s =>
                {
                    var lambda = System.Linq.Expressions.Expression.Lambda<BsonExpressionScalarDelegate>(body, context.Source, context.Root, context.Current, context.Collation, context.Parameters);

                    return lambda.Compile();
                });

                expr._funcScalar = cached;
            }
            else
            {
                var cached = _cacheEnumerable.GetOrAdd(cacheKey, s =>
                {
                    var lambda = System.Linq.Expressions.Expression.Lambda<BsonExpressionEnumerableDelegate>(body, context.Source, context.Root, context.Current, context.Collation, context.Parameters);

                    return lambda.Compile();
                });

                expr._funcEnumerable = cached;
            }

            // compile child expressions (left/right)
            if (expr.Left != null) Compile(expr.Left, context, registry);
            if (expr.Right != null) Compile(expr.Right, context, registry);
        }

        private static Expression NormalizeParameters(Expression expression, ExpressionContext context)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            if (context == null) throw new ArgumentNullException(nameof(context));

            return new ParameterNormalizer(context).Visit(expression);
        }

        private sealed class ParameterNormalizer : ExpressionVisitor
        {
            private readonly ExpressionContext _context;

            public ParameterNormalizer(ExpressionContext context)
            {
                _context = context ?? throw new ArgumentNullException(nameof(context));
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (node == null)
                {
                    return base.VisitParameter(node);
                }

                if (node == _context.Source || node == _context.Root || node == _context.Current || node == _context.Collation || node == _context.Parameters)
                {
                    return node;
                }

                if (string.Equals(node.Name, "source", StringComparison.OrdinalIgnoreCase) && node.Type == typeof(IEnumerable<BsonDocument>))
                {
                    return _context.Source;
                }

                if (string.Equals(node.Name, "root", StringComparison.OrdinalIgnoreCase) && node.Type == typeof(BsonDocument))
                {
                    return _context.Root;
                }

                if (string.Equals(node.Name, "current", StringComparison.OrdinalIgnoreCase) && node.Type == typeof(BsonValue))
                {
                    return _context.Current;
                }

                if (string.Equals(node.Name, "collation", StringComparison.OrdinalIgnoreCase) && node.Type == typeof(Collation))
                {
                    return _context.Collation;
                }

                if (string.Equals(node.Name, "parameters", StringComparison.OrdinalIgnoreCase) && node.Type == typeof(BsonDocument))
                {
                    return _context.Parameters;
                }

                return base.VisitParameter(node);
            }
        }

        /// <summary>
        /// Set same parameter referente to all expression child (left, right)
        /// </summary>
        internal static void SetParameters(BsonExpression expr, BsonDocument parameters)
        {
            expr.Parameters = parameters;

            if (expr.Left != null) SetParameters(expr.Left, parameters);
            if (expr.Right != null) SetParameters(expr.Right, parameters);
        }

        /// <summary>
        /// Get root document $ expression
        /// </summary>
        public static BsonExpression Root = Create("$");

        #endregion

        #region MethodCall quick access

        /// <summary>
        /// Get all registered methods for BsonExpressions
        /// </summary>
        public static IEnumerable<MethodInfo> Methods => _methods.Values;

        /// <summary>
        /// Load all static methods from BsonExpressionMethods class. Use a dictionary using name + parameter count
        /// </summary>
        private static readonly Dictionary<string, MethodInfo> _methods =
            typeof(BsonExpressionMethods).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(m => m.Name.ToUpperInvariant() + "~" + m.GetParameters().Where(p => p.ParameterType != typeof(Collation)).Count());

        /// <summary>
        /// Get expression method with same name and same parameter - return null if not found
        /// </summary>
        internal static MethodInfo GetMethod(string name, int parameterCount)
        {
            var key = name.ToUpperInvariant() + "~" + parameterCount;

            return _methods.GetOrDefault(key);
        }

        #endregion

        #region FunctionCall quick access

        /// <summary>
        /// Get all registered functions for BsonExpressions
        /// </summary>
        public static IEnumerable<MethodInfo> Functions => _functions.Values;

        /// <summary>
        /// Load all static methods from BsonExpressionFunctions class. Use a dictionary using name + parameter count
        /// </summary>
        private static readonly Dictionary<string, MethodInfo> _functions =
            typeof(BsonExpressionFunctions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(m => m.Name.ToUpperInvariant() + "~" + m.GetParameters()
            .Skip(5).Count());

        /// <summary>
        /// Get expression function with same name and same parameter - return null if not found
        /// </summary>
        internal static MethodInfo GetFunction(string name, int parameterCount = 0)
        {
            var key = name.ToUpperInvariant() + "~" + parameterCount;

            return _functions.GetOrDefault(key);
        }

        #endregion

        public override string ToString()
        {
            return $"`{this.Source}` [{this.Type}]";
        }

    }
}