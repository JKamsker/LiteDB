using System.Collections.Generic;

namespace LiteDB.Engine
{
    public partial class LiteEngine
    {
        private IEnumerable<BsonDocument> SysPlugins()
        {
            var transaction = _monitor.GetThreadTransaction();
            var ownsTransaction = false;

            if (transaction == null)
            {
                transaction = _monitor.GetTransaction(true, queryOnly: true, out _);
                ownsTransaction = true;
            }

            try
            {
                var scanner = new PluginRequirementScanner(_header, _disk, _walIndex, _plugins);
                var requirements = scanner.Scan(transaction?.Pages);

                foreach (var requirement in requirements)
                {
                    yield return requirement.ToDocument();
                }
            }
            finally
            {
                if (ownsTransaction)
                {
                    _monitor.ReleaseTransaction(transaction);
                }
            }
        }
    }
}

