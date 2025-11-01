using LiteDB.Engine;
using LiteDB.Plugins;
using System;
using System.Collections.Generic;
using static LiteDB.Constants;

namespace LiteDB
{
    public sealed partial class LiteCollection<T> : ILiteCollection<T>
    {
        private readonly string _collection;
        private readonly ILiteEngine _engine;
        private readonly LiteDatabase _database;
        private readonly List<BsonExpression> _includes;
        private readonly BsonMapper _mapper;
        private readonly EntityMapper _entity;
        private readonly MemberMapper _id;
        private readonly BsonAutoId _autoId;
        private readonly IExpressionRegistry _expressions;
        private readonly ILinqResolverRegistry _linqResolvers;
        private readonly IIndexInterceptorRegistry _indexInterceptors;

        /// <summary>
        /// Get collection name
        /// </summary>
        public string Name => _collection;

        /// <summary>
        /// Get collection auto id type
        /// </summary>
        public BsonAutoId AutoId => _autoId;

        /// <summary>
        /// Getting entity mapper from current collection. Returns null if collection are BsonDocument type
        /// </summary>
        public EntityMapper EntityMapper => _entity;

        internal LiteCollection(string name, BsonAutoId autoId, ILiteEngine engine, BsonMapper mapper, IExpressionRegistry expressions, LiteDatabase database, ILinqResolverRegistry linqResolvers, IIndexInterceptorRegistry indexInterceptors)
        {
            _collection = name ?? mapper.ResolveCollectionName(typeof(T));
            _engine = engine;
            _mapper = mapper;
            _includes = new List<BsonExpression>();
            _expressions = expressions;
            _database = database;
            _linqResolvers = linqResolvers;
            _indexInterceptors = indexInterceptors;

            // if strong typed collection, get _id member mapped (if exists)
            if (typeof(T) == typeof(BsonDocument))
            {
                _entity = null;
                _id = null;
                _autoId = autoId;
            }
            else
            {
                _entity = mapper.GetEntityMapper(typeof(T));
                _entity.WaitForInitialization();
                
                _id = _entity.Id;

                if (_id != null && _id.AutoId)
                {
                    _autoId =
                        _id.DataType == typeof(Int32) || _id.DataType == typeof(Int32?) ? BsonAutoId.Int32 :
                        _id.DataType == typeof(Int64) || _id.DataType == typeof(Int64?) ? BsonAutoId.Int64 :
                        _id.DataType == typeof(Guid) || _id.DataType == typeof(Guid?) ? BsonAutoId.Guid :
                        BsonAutoId.ObjectId;
                }
                else
                {
                    _autoId = autoId;
                }
            }
        }

        private BsonExpression CreateExpression(string expression, BsonDocument parameters)
        {
            return BsonExpression.Create(expression, parameters, _expressions);
        }

        private BsonExpression CreateExpression(string expression, params BsonValue[] args)
        {
            return BsonExpression.Create(expression, _expressions, args ?? Array.Empty<BsonValue>());
        }
    }
}
