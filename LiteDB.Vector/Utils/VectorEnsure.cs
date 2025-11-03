using LiteDB;
using System.Diagnostics;

namespace LiteDB.Vector.Utils
{
    internal static class VectorEnsure
    {
        internal static void ENSURE(bool conditional, string message = null)
        {
            if (!conditional)
            {
                if (Debugger.IsAttached)
                {
                    Debugger.Break();
                }

                throw new LiteException(0, message ?? "Vector invariant failed.");
            }
        }

        internal static void ENSURE(bool conditional, string format, params object[] args)
        {
            ENSURE(conditional, string.Format(format, args));
        }

        internal static void ENSURE(bool ifTest, bool conditional, string message = null)
        {
            if (ifTest)
            {
                ENSURE(conditional, message);
            }
        }
    }
}
