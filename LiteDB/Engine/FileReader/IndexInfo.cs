using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LiteDB.Plugins;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    internal class IndexInfo
    {
        public string Collection { get; set; }
        public string Name { get; set; }
        public string Expression { get; set; }
        public bool Unique { get; set; }
        public byte IndexType { get; set; }
        public string PluginId { get; set; }
        public string PluginIndexKind { get; set; }
        public byte[] PluginMetadata { get; set; }
        public BsonDocument PluginMetadataDocument { get; set; }
        public BsonExpression BsonExpr { get; private set; }
        public IExpressionRegistry Registry { get; private set; }

        public void BindExpressionRegistry(IExpressionRegistry registry)
        {
            var effective = registry ?? PluginContextFallbacks.Expressions;

            this.Registry = effective;
            this.BsonExpr = BsonExpression.Create(this.Expression, effective);
        }
    }
}
