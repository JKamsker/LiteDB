using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Plugins.Indexing;
using LiteDB.Vector;

namespace LiteDB.Vector.Tools
{
    /// <summary>
    /// Provides helpers used by the vector upgrade tooling to inspect legacy databases,
    /// relocate vector metadata, and validate plugin readiness.
    /// </summary>
    public static class MigrationHelpers
    {
        private static readonly Type PageBufferType = typeof(LiteEngine).Assembly.GetType("LiteDB.Engine.PageBuffer")
            ?? throw new InvalidOperationException("Unable to locate LiteDB.Engine.PageBuffer type.");

        private static readonly Type CollectionPageType = typeof(LiteEngine).Assembly.GetType("LiteDB.Engine.CollectionPage")
            ?? throw new InvalidOperationException("Unable to locate LiteDB.Engine.CollectionPage type.");

        /// <summary>
        /// Captures all vector index definitions stored in the supplied database file.
        /// </summary>
        /// <param name="databasePath">Path to the LiteDB data file.</param>
        /// <returns>Immutable collection describing discovered vector indexes.</returns>
        public static IReadOnlyList<VectorIndexSchema> CaptureVectorIndexes(string databasePath)
        {
            var fullPath = NormalizeDatabasePath(databasePath);

            using var engine = OpenReadOnlyEngine(fullPath);

            return ExtractVectorIndexes(engine);
        }

        /// <summary>
        /// Runs the metadata relocation workflow for legacy vector indexes and validates plugin readiness.
        /// </summary>
        /// <param name="options">Options describing how the migration should run.</param>
        /// <returns>Report summarising the actions performed.</returns>
        public static VectorMigrationReport RelocateVectorMetadata(VectorMigrationOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var databasePath = NormalizeDatabasePath(options.DatabasePath);

            var before = CaptureVectorIndexes(databasePath);

            string backupPath = null;
            if (options.CreateBackup && !options.DryRun)
            {
                backupPath = CreateBackup(databasePath, options.BackupDirectory, options.OverwriteBackup);
            }

            if (options.DryRun)
            {
                var pluginStatus = ValidatePlugin(databasePath);
                return new VectorMigrationReport(
                    before,
                    before,
                    Array.Empty<string>(),
                    new[] { "Dry-run requested. No changes applied." },
                    pluginStatus,
                    backupPath,
                    rebuildExecuted: false,
                    pagesRebuilt: 0);
            }

            long pagesRebuilt = 0;
            PluginValidationResult pluginValidation;

            using (var database = OpenDatabase(databasePath))
            {
                pluginValidation = CapturePluginStatus(database);

                if (!pluginValidation.PluginRegistered)
                {
                    throw new InvalidOperationException("VectorSearchPlugin is not registered. Ensure the LiteDB.Vector plugin assembly is loaded before running the upgrade.");
                }

                if (options.RunRebuild)
                {
                    pagesRebuilt = database.Rebuild();
                }
            }

            var after = CaptureVectorIndexes(databasePath);

            var actions = new List<string>();
            if (options.RunRebuild)
            {
                actions.Add($"Executed LiteDatabase.Rebuild (pages rebuilt: {pagesRebuilt}).");
            }
            else
            {
                actions.Add("Skipped rebuild per options; database left unchanged.");
            }

            var warnings = new List<string>();

            if (before.Count != after.Count)
            {
                warnings.Add($"Vector index count changed from {before.Count} to {after.Count}. Review the database for missing indexes.");
            }

            var missing = before.Where(b => !after.Any(a => a.Collection == b.Collection && a.Name == b.Name)).ToArray();
            if (missing.Length > 0)
            {
                warnings.Add($"Missing vector indexes detected after migration: {string.Join(", ", missing.Select(x => $"{x.Collection}.{x.Name}"))}");
            }

            foreach (var entry in before)
            {
                var match = after.FirstOrDefault(a => a.Collection == entry.Collection && a.Name == entry.Name);
                if (match == null)
                {
                    continue;
                }

                if (entry.Dimensions != match.Dimensions || entry.MetricCode != match.MetricCode)
                {
                    warnings.Add($"Vector index options changed for {entry.Collection}.{entry.Name}: dimensions {entry.Dimensions}->{match.Dimensions}, metric {entry.MetricCode}->{match.MetricCode}.");
                }
            }

            return new VectorMigrationReport(
                before,
                after,
                actions,
                warnings,
                pluginValidation,
                backupPath,
                options.RunRebuild,
                pagesRebuilt);
        }

        /// <summary>
        /// Validates plugin registration without modifying the database.
        /// </summary>
        /// <param name="databasePath">Path to the LiteDB data file.</param>
        /// <returns>Status describing plugin readiness.</returns>
        public static PluginValidationResult ValidatePlugin(string databasePath)
        {
            var fullPath = NormalizeDatabasePath(databasePath);

            using var database = OpenDatabase(fullPath);
            return CapturePluginStatus(database);
        }

        private static PluginValidationResult CapturePluginStatus(LiteDatabase database)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            var registry = database.Services.CustomIndexes;

            if (registry == null)
            {
                return new PluginValidationResult(false, false, Array.Empty<string>());
            }

            var registered = registry.Registered?.Select(r => r.StrategyId).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
                ?? Array.Empty<string>();

            var hasDefault = registry.TryGet("LiteDB.Vector", out _);

            return new PluginValidationResult(true, hasDefault, registered);
        }

        private static LiteDatabase OpenDatabase(string databasePath)
        {
            var connectionString = new ConnectionString
            {
                Filename = databasePath,
                Connection = ConnectionType.Shared
            };

            return new LiteDatabase(connectionString, plugins: new[] { VectorSearchPlugin.Instance });
        }

        private static LiteEngine OpenReadOnlyEngine(string databasePath)
        {
            var settings = new EngineSettings
            {
                Filename = databasePath,
                ReadOnly = true,
                SharedMutexNameStrategy = SharedMutexNameStrategy.Sha1Hash
            };

            return new LiteEngine(settings);
        }

        private static IReadOnlyList<VectorIndexSchema> ExtractVectorIndexes(LiteEngine engine)
        {
            if (engine == null)
            {
                throw new ArgumentNullException(nameof(engine));
            }

            var indexes = new List<VectorIndexSchema>();
            var where = BsonExpression.Create("pageType = 'Collection'");
            var query = new LiteDB.Query();
            query.Where.Add(where);

            using (var reader = engine.Query("$dump", query))
            {
                while (reader.Read())
                {
                    var doc = reader.Current.AsDocument;
                    if (!doc.TryGetValue("pageID", out var pageValue) || !pageValue.IsNumber)
                    {
                        continue;
                    }

                    var pageId = pageValue.AsInt32;
                    var collection = doc.TryGetValue("collection", out var collectionValue) && collectionValue.IsString
                        ? collectionValue.AsString
                        : string.Empty;

                    foreach (var schema in ExtractPageVectorIndexes(engine, pageId, collection))
                    {
                        indexes.Add(schema);
                    }
                }
            }

            return indexes;
        }

        private static IEnumerable<VectorIndexSchema> ExtractPageVectorIndexes(LiteEngine engine, int pageId, string collectionName)
        {
            using var reader = engine.Query($"$dump({pageId})", new LiteDB.Query());

            while (reader.Read())
            {
                var doc = reader.Current.AsDocument;

                if (!doc.TryGetValue("buffer", out var bufferValue) || !bufferValue.IsBinary)
                {
                    continue;
                }

                foreach (var metadata in ParseVectorMetadata(bufferValue.AsBinary))
                {
                    yield return new VectorIndexSchema(
                        collectionName,
                        metadata.IndexName,
                        metadata.Expression,
                        metadata.Dimensions,
                        metadata.MetricCode,
                        metadata.Slot);
                }
            }
        }

        private static IEnumerable<VectorMetadataSnapshot> ParseVectorMetadata(byte[] buffer)
        {
            if (buffer == null)
            {
                yield break;
            }

            var working = new byte[buffer.Length];
            Buffer.BlockCopy(buffer, 0, working, 0, buffer.Length);

            var pageBuffer = Activator.CreateInstance(
                PageBufferType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object[] { working, 0, 0 },
                culture: CultureInfo.InvariantCulture);

            if (pageBuffer == null)
            {
                yield break;
            }

            var collectionPage = Activator.CreateInstance(
                CollectionPageType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new[] { pageBuffer },
                culture: CultureInfo.InvariantCulture);

            if (collectionPage == null)
            {
                yield break;
            }

            var method = CollectionPageType.GetMethod("GetVectorIndexes", BindingFlags.Instance | BindingFlags.Public);
            if (method == null)
            {
                yield break;
            }

            if (method.Invoke(collectionPage, null) is not IEnumerable enumerable)
            {
                yield break;
            }

            foreach (var entry in enumerable)
            {
                if (entry == null)
                {
                    continue;
                }

                var tupleType = entry.GetType();
                var indexField = tupleType.GetField("Item1");
                var metadataField = tupleType.GetField("Item2");

                if (indexField == null || metadataField == null)
                {
                    continue;
                }

                var index = indexField.GetValue(entry);
                var metadataBytes = metadataField.GetValue(entry) as byte[];

                if (index == null || metadataBytes == null)
                {
                    continue;
                }

                var indexType = index.GetType();

                var name = indexType.GetProperty("Name")?.GetValue(index) as string ?? string.Empty;
                var expression = indexType.GetProperty("Expression")?.GetValue(index) as string ?? string.Empty;
                var slotValue = indexType.GetProperty("Slot")?.GetValue(index);

                var slot = slotValue is byte slotByte ? slotByte : (byte)0;
                var dimensions = VectorIndexMetadataSerializer.GetDimensions(metadataBytes);
                var metricCode = VectorIndexMetadataSerializer.GetMetric(metadataBytes);

                yield return new VectorMetadataSnapshot(name, expression, slot, dimensions, metricCode);
            }
        }

        private static string NormalizeDatabasePath(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentException("Database path must be provided.", nameof(databasePath));
            }

            var fullPath = Path.GetFullPath(databasePath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("LiteDB data file not found.", fullPath);
            }

            return fullPath;
        }

        private static string CreateBackup(string databasePath, string backupDirectory, bool overwrite)
        {
            var directory = ResolveBackupDirectory(backupDirectory);

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            var baseName = Path.GetFileNameWithoutExtension(databasePath);
            var extension = Path.GetExtension(databasePath);
            var backupFile = Path.Combine(directory, $"{baseName}.vector-backup.{timestamp}{extension}");

            File.Copy(databasePath, backupFile, overwrite);

            CopySidecarIfExists(databasePath, backupFile, "-log", overwrite);
            CopySidecarIfExists(databasePath, backupFile, "-lock", overwrite);

            return backupFile;
        }

        private static void CopySidecarIfExists(string sourcePath, string backupPath, string suffix, bool overwrite)
        {
            var source = BuildSidecarPath(sourcePath, suffix);
            if (!File.Exists(source))
            {
                return;
            }

            var target = BuildSidecarPath(backupPath, suffix);
            File.Copy(source, target, overwrite);
        }

        private static string BuildSidecarPath(string path, string suffix)
        {
            var directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
            var name = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            return Path.Combine(directory, $"{name}{suffix}{extension}");
        }

        private static string ResolveBackupDirectory(string backupDirectory)
        {
            if (!string.IsNullOrWhiteSpace(backupDirectory))
            {
                var normalized = Path.GetFullPath(backupDirectory);
                Directory.CreateDirectory(normalized);
                return normalized;
            }

            var fallback = Path.Combine(Path.GetTempPath(), "LiteDB", "vector-upgrade", "databases");
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        private readonly record struct VectorMetadataSnapshot(
            string IndexName,
            string Expression,
            byte Slot,
            ushort Dimensions,
            byte MetricCode);
    }

    /// <summary>
    /// Options supplied to the migration helper when orchestrating metadata relocation.
    /// </summary>
    public sealed class VectorMigrationOptions
    {
        /// <summary>
        /// Absolute or relative path to the LiteDB data file.
        /// </summary>
        public string DatabasePath { get; set; }

        /// <summary>
        /// Optional directory where backups will be written.
        /// </summary>
        public string BackupDirectory { get; set; }

        /// <summary>
        /// Indicates whether a backup should be created before mutating the database.
        /// </summary>
        public bool CreateBackup { get; set; } = true;

        /// <summary>
        /// Indicates whether existing backups with the same name should be overwritten.
        /// </summary>
        public bool OverwriteBackup { get; set; }

        /// <summary>
        /// When true, the helper executes LiteDatabase.Rebuild to rehydrate vector metadata using the plugin runtime.
        /// </summary>
        public bool RunRebuild { get; set; } = true;

        /// <summary>
        /// When true, no changes are applied; the helper only inspects the current state.
        /// </summary>
        public bool DryRun { get; set; }
    }

    /// <summary>
    /// Describes the vector indexes discovered during migration analysis.
    /// </summary>
    public sealed class VectorIndexSchema
    {
        internal VectorIndexSchema(
            string collection,
            string name,
            string expression,
            ushort dimensions,
            byte metricCode,
            byte slot)
        {
            Collection = collection;
            Name = name;
            Expression = expression;
            Dimensions = dimensions;
            MetricCode = metricCode;
            MetricName = Enum.IsDefined(typeof(VectorDistanceMetric), (VectorDistanceMetric)metricCode)
                ? ((VectorDistanceMetric)metricCode).ToString()
                : $"Unknown({metricCode})";
            Slot = slot;
        }

        /// <summary>
        /// Gets the collection name hosting the index.
        /// </summary>
        public string Collection { get; }

        /// <summary>
        /// Gets the index name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the serialized Bson expression associated with the index.
        /// </summary>
        public string Expression { get; }

        /// <summary>
        /// Gets the vector dimensionality stored in metadata.
        /// </summary>
        public ushort Dimensions { get; }

        /// <summary>
        /// Gets the raw metric identifier as stored on disk.
        /// </summary>
        public byte MetricCode { get; }

        /// <summary>
        /// Gets the symbolic metric name when recognised.
        /// </summary>
        public string MetricName { get; }

        /// <summary>
        /// Gets the collection slot associated with the index.
        /// </summary>
        public byte Slot { get; }
    }

    /// <summary>
    /// Summarises the actions carried out by the migration helper.
    /// </summary>
    public sealed class VectorMigrationReport
    {
        internal VectorMigrationReport(
            IReadOnlyList<VectorIndexSchema> before,
            IReadOnlyList<VectorIndexSchema> after,
            IReadOnlyList<string> actions,
            IReadOnlyList<string> warnings,
            PluginValidationResult pluginValidation,
            string backupPath,
            bool rebuildExecuted,
            long pagesRebuilt)
        {
            Before = before ?? Array.Empty<VectorIndexSchema>();
            After = after ?? Array.Empty<VectorIndexSchema>();
            Actions = actions ?? Array.Empty<string>();
            Warnings = warnings ?? Array.Empty<string>();
            PluginValidation = pluginValidation ?? new PluginValidationResult(false, false, Array.Empty<string>());
            BackupPath = backupPath;
            RebuildExecuted = rebuildExecuted;
            PagesRebuilt = pagesRebuilt;
            TimestampUtc = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Gets the vector index snapshot captured before migration.
        /// </summary>
        public IReadOnlyList<VectorIndexSchema> Before { get; }

        /// <summary>
        /// Gets the vector index snapshot captured after migration.
        /// </summary>
        public IReadOnlyList<VectorIndexSchema> After { get; }

        /// <summary>
        /// Gets the list of actions executed during migration.
        /// </summary>
        public IReadOnlyList<string> Actions { get; }

        /// <summary>
        /// Gets any warnings surfaced by the helper.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// Gets the plugin validation result observed during migration.
        /// </summary>
        public PluginValidationResult PluginValidation { get; }

        /// <summary>
        /// Gets the path to the backup file when one was created.
        /// </summary>
        public string BackupPath { get; }

        /// <summary>
        /// Indicates whether LiteDatabase.Rebuild executed during migration.
        /// </summary>
        public bool RebuildExecuted { get; }

        /// <summary>
        /// Gets the number of pages rewritten by the rebuild command.
        /// </summary>
        public long PagesRebuilt { get; }

        /// <summary>
        /// Gets the UTC timestamp when the report was generated.
        /// </summary>
        public DateTimeOffset TimestampUtc { get; }
    }

    /// <summary>
    /// Describes plugin registration status captured during migration.
    /// </summary>
    public sealed class PluginValidationResult
    {
        internal PluginValidationResult(bool pluginRegistered, bool defaultStrategyRegistered, IReadOnlyList<string> registeredStrategies)
        {
            PluginRegistered = pluginRegistered;
            DefaultStrategyRegistered = defaultStrategyRegistered;
            RegisteredStrategies = registeredStrategies ?? Array.Empty<string>();
        }

        /// <summary>
        /// Gets a value indicating whether any vector plugin strategy was registered.
        /// </summary>
        public bool PluginRegistered { get; }

        /// <summary>
        /// Gets a value indicating whether the default LiteDB.Vector strategy is available.
        /// </summary>
        public bool DefaultStrategyRegistered { get; }

        /// <summary>
        /// Gets the collection of registered strategy identifiers.
        /// </summary>
        public IReadOnlyList<string> RegisteredStrategies { get; }
    }
}

