﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    /// <summary>
    /// [ThreadSafe]
    /// </summary>
    internal class RebuildService
    {
        private readonly EngineSettings _settings;
        private readonly int _fileVersion;
        private IList<FileReaderError> _errors;

        public RebuildService(EngineSettings settings)
        {
            _settings = settings;

            // open, read first 16kb, and close data file
            var buffer = this.ReadFirstBytes();

            // test for valid reader to use
            _fileVersion = 
                FileReaderV7.IsVersion(buffer) ? 7 :
                FileReaderV8.IsVersion(buffer) ? 8 : throw LiteException.InvalidDatabase();

        }

        public long Rebuild(RebuildOptions options)
        {
            var backupFilename = FileHelper.GetSufixFile(_settings.Filename, "-backup", true);
            var tempFilename = FileHelper.GetSufixFile(_settings.Filename, "-temp", true);

            // open file reader
            var reader = _fileVersion == 7 ? 
                new FileReaderV7(_settings) : 
                (IFileReader)new FileReaderV8(_settings, options.Errors);

            // open file reader and ready to import to new temp engine instance
            reader.Open();

            // open new engine to recive all data readed from FileReader
            using (var engine = new LiteEngine(new EngineSettings
            {
                Filename = tempFilename,
                Collation = options.Collation,
                Password = options.Password
            }))
            {
                // copy all database to new Log file with NO checkpoint during all rebuild
                engine.Pragma(Pragmas.CHECKPOINT, 0);

                // rebuild all content from reader into new engine
                engine.RebuildContent(reader);

                // insert error report
                if (options.IncludeErrorReport && options.Errors.Count > 0)
                {
                    // a random buildId to group by event
                    var buildId = Guid.NewGuid().ToString("d").ToLower().Substring(6);

                    var docs = options.Errors.Select(x => new BsonDocument
                    {
                        ["buildId"] = buildId,
                        ["created"] = x.Created,
                        ["pageID"] = (int)x.PageID,
                        ["code"] = x.Code,
                        ["field"] = x.Field,
                        ["message"] = x.Message,
                    });

                    engine.Insert("_rebuild_errors", docs, BsonAutoId.Int32);
                }

                // after rebuild, copy log bytes into data file
                engine.Checkpoint();

                // update pragmas
                var pragmas = reader.GetPragmas();

                engine.Pragma(Pragmas.CHECKPOINT, pragmas[Pragmas.CHECKPOINT]);
                engine.Pragma(Pragmas.TIMEOUT, pragmas[Pragmas.TIMEOUT]);
                engine.Pragma(Pragmas.LIMIT_SIZE, pragmas[Pragmas.LIMIT_SIZE]);
                engine.Pragma(Pragmas.UTC_DATE, pragmas[Pragmas.UTC_DATE]);
                engine.Pragma(Pragmas.USER_VERSION, pragmas[Pragmas.USER_VERSION]);
            }

            // rename source filename to backup name
            File.Move(tempFilename, backupFilename);

            // rename temp file into filename
            File.Move(_settings.Filename, tempFilename);

            // get difference size
            return 
                new FileInfo(backupFilename).Length -
                new FileInfo(_settings.Filename).Length;
        }

        /// <summary>
        /// Mark a file with a single signal to next open do auto-rebuild
        /// </summary>
        public void MarkAsInvalidState()
        {
            var factory = _settings.CreateDataFactory();
            var timeout = TimeSpan.FromSeconds(60);

            FileHelper.TryExec(() =>
            {
                using (var stream = factory.GetStream(true, true, true))
                {
                    stream.Position = HeaderPage.P_INVALID_DATAFILE_STATE;
                    stream.Write(new byte[] { 1 }, 0, 1);
                    stream.FlushToDisk();
                }
            }, timeout);
        }

        /// <summary>
        /// Read first 16bk (2 PAGES) in bytes
        /// </summary>
        private byte[] ReadFirstBytes()
        {
            var buffer = new byte[PAGE_SIZE * 2];
            var factory = _settings.CreateDataFactory();

            using (var stream = factory.GetStream(false, false, true))
            {
                stream.Position = 0;
                stream.Read(buffer, 0, buffer.Length);
            }

            return buffer;
        }
    }
}