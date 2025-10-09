using System;
using System.Linq;
using System.Reflection;
using LiteDB.Spatial.Testing.Oracles.Internal;
using Xunit.Sdk;

namespace LiteDB.Spatial.Testing.Oracles.Xunit
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class OracleSkipAttribute : BeforeAfterTestAttribute
    {
        private readonly string[] _requiredOracles;
        private readonly bool _requiresDatabase;

        public OracleSkipAttribute(params string[] requiredOracles)
            : this(false, requiredOracles)
        {
        }

        public OracleSkipAttribute(bool requiresDatabase, params string[] requiredOracles)
        {
            _requiresDatabase = requiresDatabase;
            _requiredOracles = requiredOracles?.Where(static name => !string.IsNullOrWhiteSpace(name)).Select(static name => name!).ToArray() ?? Array.Empty<string>();
        }

        public override void Before(MethodInfo methodUnderTest)
        {
            if (_requiresDatabase && !OracleEnvironment.AreDatabaseTestsEnabled())
            {
                throw SkipException.ForSkip("Spatial database tests are disabled. Set SPATIAL_DB_TESTS=1 to enable.");
            }

            foreach (var oracle in _requiredOracles)
            {
                var normalized = OracleEnvironment.Normalize(oracle);
                if (normalized is null)
                {
                    continue;
                }

                if (!OracleEnvironment.RequiresOracle(normalized))
                {
                    throw SkipException.ForSkip($"Oracle '{normalized}' is disabled via SPATIAL_ORACLES.");
                }
            }
        }
    }
}
