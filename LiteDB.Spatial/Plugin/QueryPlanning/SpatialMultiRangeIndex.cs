extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using LiteDB.Spatial;
using LiteDbEngine = LiteDbBase::LiteDB.Engine;
using LiteDbQuery = LiteDbBase::LiteDB.Query;

namespace LiteDB.Spatial.Plugin.QueryPlanning
{
    internal sealed class SpatialMultiRangeIndex : LiteDbEngine.Index
    {
        private readonly IReadOnlyList<SpatialIndexRange> _ranges;

        public SpatialMultiRangeIndex(string name, IReadOnlyList<SpatialIndexRange> ranges, int order = LiteDbQuery.Ascending)
            : base(name ?? throw new ArgumentNullException(nameof(name)), order)
        {
            _ranges = ranges ?? throw new ArgumentNullException(nameof(ranges));
        }

        public override uint GetCost(LiteDbEngine.CollectionIndex index)
        {
            if (_ranges.Count == 0)
            {
                return uint.MaxValue;
            }

            var baseCost = 10u;
            var rangeCost = (uint)Math.Min(_ranges.Count * 5, 50);
            return baseCost + rangeCost;
        }

        public override IEnumerable<LiteDbEngine.IndexNode> Execute(LiteDbEngine.IndexService indexer, LiteDbEngine.CollectionIndex index)
        {
            foreach (var range in _ranges)
            {
                var start = CreateIndexValue(range.Start);
                var end = CreateIndexValue(range.End);
                var rangeQuery = new LiteDbEngine.IndexRange(Name, start, end, true, true, Order);

                foreach (var node in rangeQuery.Execute(indexer, index))
                {
                    yield return node;
                }
            }
        }

        private static LiteDbBase::LiteDB.BsonValue CreateIndexValue(ulong value)
        {
            if (value <= long.MaxValue)
            {
                return new LiteDbBase::LiteDB.BsonValue((long)value);
            }

            return new LiteDbBase::LiteDB.BsonValue((decimal)value);
        }
    }
}
