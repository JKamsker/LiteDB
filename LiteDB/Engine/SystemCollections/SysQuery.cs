using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LiteDB.Plugins;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    /// <summary>
    /// This class implement $query experimental system function to run sub-queries. It's experimental only - possible not be present in final release
    /// </summary>
    internal class SysQuery : SystemCollection
    {
        private readonly LiteEngine _engine;
        private readonly IExpressionRegistry _expressions;

        public SysQuery(LiteEngine engine) : base("$query")
        {
            _engine = engine;
            _expressions = engine.PluginContext?.Expressions;
        }

        public override IEnumerable<BsonDocument> Input(BsonValue options)
        {
            var query = options?.AsString ?? throw new LiteException(0, $"Collection $query(sql) requires `sql` string parameter");

            var sql = new SqlParser(_engine, new Tokenizer(query), null, _expressions);

            using (var reader = sql.Execute())
            {
                while(reader.Read())
                {
                    var value = reader.Current;

                    yield return value.IsDocument ? value.AsDocument : new BsonDocument { ["expr"] = value };
                }
            }
        }
    }
}