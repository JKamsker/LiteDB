using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using LiteDB.Engine;

namespace LiteDB
{
    public partial class LiteCollection<T>
    {
        /// <summary>
        /// Create a new permanent index in all documents inside this collections if index not exists already. Returns true if index was created or false if already exits
        /// </summary>
        /// <param name="name">Index name - unique name for this collection</param>
        /// <param name="expression">Create a custom expression function to be indexed</param>
        /// <param name="unique">If is a unique index</param>
        public bool EnsureIndex(string name, BsonExpression expression, bool unique = false)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (expression == null) throw new ArgumentNullException(nameof(expression));

            var pluginContext = _database?.Services?.Context;
            var interceptorRegistry = pluginContext?.EnsureIndexInterceptors;

            if (interceptorRegistry?.Count > 0)
            {
                var interceptors = interceptorRegistry.Interceptors;

                bool DefaultHandler(string resolvedName, BsonExpression resolvedExpression, bool resolvedUnique)
                {
                    return _engine.EnsureIndex(_collection, resolvedName, resolvedExpression, resolvedUnique);
                }

                var ensureContext = new Plugins.EnsureIndexContext(
                    _database,
                    _engine,
                    typeof(T),
                    _collection,
                    name,
                    expression,
                    unique,
                    _mapper,
                    pluginContext,
                    DefaultHandler);

                foreach (var interceptor in interceptors)
                {
                    if (interceptor == null)
                    {
                        continue;
                    }

                    if (interceptor.TryHandleEnsureIndex(ensureContext))
                    {
                        break;
                    }

                    if (ensureContext.DefaultExecuted || ensureContext.Result.HasValue)
                    {
                        break;
                    }
                }

                if (!ensureContext.Result.HasValue)
                {
                    ensureContext.ExecuteDefault();
                }

                return ensureContext.Result.GetValueOrDefault();
            }

            return _engine.EnsureIndex(_collection, name, expression, unique);
        }

        /// <summary>
        /// Create a new permanent index in all documents inside this collections if index not exists already. Returns true if index was created or false if already exits
        /// </summary>
        /// <param name="expression">Document field/expression</param>
        /// <param name="unique">If is a unique index</param>
        public bool EnsureIndex(BsonExpression expression, bool unique = false)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));

            var name = Regex.Replace(expression.Source, @"[^a-z0-9]", "", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            return this.EnsureIndex(name, expression, unique);
        }

        /// <summary>
        /// Create a new permanent index in all documents inside this collections if index not exists already.
        /// </summary>
        /// <param name="keySelector">LinqExpression to be converted into BsonExpression to be indexed</param>
        /// <param name="unique">Create a unique keys index?</param>
        public bool EnsureIndex<K>(Expression<Func<T, K>> keySelector, bool unique = false)
        {
            var expression = this.GetIndexExpression(keySelector);

            return this.EnsureIndex(expression, unique);
        }

        /// <summary>
        /// Create a new permanent index in all documents inside this collections if index not exists already.
        /// </summary>
        /// <param name="name">Index name - unique name for this collection</param>
        /// <param name="keySelector">LinqExpression to be converted into BsonExpression to be indexed</param>
        /// <param name="unique">Create a unique keys index?</param>
        public bool EnsureIndex<K>(string name, Expression<Func<T, K>> keySelector, bool unique = false)
        {
            var expression = this.GetIndexExpression(keySelector);

            return this.EnsureIndex(name, expression, unique);
        }

        /// <summary>
        /// Get index expression based on LINQ expression. Convert IEnumerable in MultiKey indexes
        /// </summary>
        internal BsonExpression GetIndexExpression<K>(Expression<Func<T, K>> keySelector, bool convertEnumerableToMultiKey = true)
        {
            var expression = _mapper.GetIndexExpression(keySelector, _expressions, _database, _linqResolvers);

            if (convertEnumerableToMultiKey && typeof(K).IsEnumerable() && expression.IsScalar == true)
            {
                if (expression.Type == BsonExpressionType.Path)
                {
                    // convert LINQ expression that returns an IEnumerable but expression returns a single value
                    // `x => x.Phones` --> `$.Phones[*]`
                    // works only if exression is a simple path
                    expression = expression.Source + "[*]";
                }
                else
                {
                    throw new LiteException(0, $"Expression `{expression.Source}` must return a enumerable expression");
                }
            }

            return expression;
        }

        /// <summary>
        /// Drop index and release slot for another index
        /// </summary>
        public bool DropIndex(string name)
        {
            return _engine.DropIndex(_collection, name);
        }

    }
}
