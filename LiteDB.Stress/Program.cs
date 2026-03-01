using LiteDB;
using LiteDB.Engine;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDB.Stress
{
    public class Program
    {
        static void Main(string[] args)
        {
            var actualArgs = args ?? Array.Empty<string>();
            var suppressWait = actualArgs.Any(arg => string.Equals(arg, "--no-wait", StringComparison.OrdinalIgnoreCase));
            var sanitizedArgs = actualArgs
                .Where(arg => !string.Equals(arg, "--no-wait", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var filename = sanitizedArgs.Length >= 1 ? sanitizedArgs[0] : string.Empty;
            var duration = TimeSpanEx.Parse(sanitizedArgs.Length >= 2 ? sanitizedArgs[1] : "60s");

            var e = new TestExecution(filename, duration);

            e.Execute();

            if (suppressWait)
            {
                var waitFor = duration + TimeSpan.FromSeconds(30);
                if (waitFor < TimeSpan.Zero)
                {
                    waitFor = duration;
                }

                Thread.Sleep(waitFor);
                return;
            }

            if (!suppressWait && Environment.UserInteractive && !Console.IsInputRedirected)
            {
                Console.ReadKey();
            }
        }
    }
}
