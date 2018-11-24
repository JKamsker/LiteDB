﻿using LiteDB;
using LiteDB.Engine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static LiteDB.Constants;

namespace LiteDB.Demo
{
    class Program
    {
        static void Main(string[] args)
        {
            var sw = new Stopwatch();

            //TestMemoryFile.Run(sw);
            //TestAesEncryption.CreateEncryptedFile(sw);
            //TestAesEncryption.ReadEncryptedFile(sw);
            //TestDataPage.Run(sw);
            TestEngine.Run(sw);

            Console.WriteLine("Elapsed: " + sw.ElapsedMilliseconds);
            Console.ReadKey();

        }
    }

}
