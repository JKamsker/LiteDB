using System;
using System.Collections.Generic;
using FluentAssertions;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Tests.Utils;
using Xunit;

namespace LiteDB.Tests.Client
{
    public class LiteDatabaseBuilderTests
    {
        [Fact]
        public void Build_should_create_database_and_initialize_plugins()
        {
            using var file = new TempFile();
            var plugin = new TestTrackingPlugin();

            using (var db = new LiteDatabaseBuilder()
                .UseFile(file.Filename)
                .UsePlugin(plugin)
                .Build())
            {
                plugin.InitializeCount.Should().Be(1);

                var collection = db.GetCollection<BsonDocument>("docs");
                collection.Insert(new BsonDocument { ["_id"] = 1 });
            }

            using var reopened = new LiteDatabase(file.Filename);
            reopened.GetCollection<BsonDocument>("docs").Count().Should().Be(1);
        }

        [Fact]
        public void Build_should_throw_when_called_twice()
        {
            var builder = new LiteDatabaseBuilder().UseInMemory();

            using var db = builder.Build();

            Action act = () => builder.Build();

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void UseFile_should_throw_when_another_data_source_is_already_configured()
        {
            var builder = new LiteDatabaseBuilder().UseInMemory();

            Action act = () => builder.UseFile("other.db");

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void Build_should_throw_when_no_data_source_is_configured()
        {
            var builder = new LiteDatabaseBuilder();

            Action act = () => builder.Build();

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void UsePlugin_factory_should_dispose_owned_instances()
        {
            DisposableTrackingPlugin first = null;
            DisposableTrackingPlugin duplicate = null;
            var logger = new CapturingLogger();

            using (var db = new LiteDatabaseBuilder()
                .WithLogger(logger)
                .UseInMemory()
                .UsePlugin(() =>
                {
                    first = new DisposableTrackingPlugin();
                    return first;
                })
                .UsePlugin(() =>
                {
                    duplicate = new DisposableTrackingPlugin();
                    return duplicate;
                })
                .Build())
            {
                first.Should().NotBeNull();
                duplicate.Should().NotBeNull();

                first.Initialized.Should().BeTrue();
                first.Disposed.Should().BeFalse();

                duplicate.Initialized.Should().BeFalse();
                duplicate.Disposed.Should().BeTrue();

                logger.Entries.Should().ContainSingle(x => x.Level == LogLevel.Warning && x.Message.Contains("registered multiple times", StringComparison.Ordinal));
            }

            first.Disposed.Should().BeTrue();
        }

        [Fact]
        public void UsePlugin_instance_should_not_be_disposed_by_database()
        {
            var plugin = new DisposableTrackingPlugin();

            using (var db = new LiteDatabaseBuilder()
                .UseInMemory()
                .UsePlugin(plugin)
                .Build())
            {
                plugin.Initialized.Should().BeTrue();
                plugin.Disposed.Should().BeFalse();
            }

            plugin.Disposed.Should().BeFalse();
        }

        [Fact]
        public void Build_should_invoke_handle_lifecycle_hooks()
        {
            var plugin = new TestTrackingPlugin();

            using var db = new LiteDatabaseBuilder()
                .UseInMemory()
                .UsePlugin(plugin)
                .Build();

            plugin.InitializeCount.Should().Be(1);
            plugin.HandleCreatedCount.Should().Be(1);
        }

        [Fact]
        public void Build_should_dispose_engine_when_plugin_factory_throws()
        {
            using var file = new TempFile();

            var builder = new LiteDatabaseBuilder()
                .UseFile(file.Filename)
                .UsePlugin(() => throw new InvalidOperationException("plugin factory failure"));

            Action act = () => builder.Build();

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*plugin factory failure*");

            using var reopened = new LiteDatabase(file.Filename);
            reopened.GetCollection<BsonDocument>("docs").Insert(new BsonDocument { ["_id"] = 1 });
            reopened.GetCollection<BsonDocument>("docs").Count().Should().Be(1);
        }

        [Fact]
        public void Registries_should_be_frozen_after_initialization()
        {
            using var db = (LiteDatabase)new LiteDatabaseBuilder()
                .UseInMemory()
                .Build();

            Action act = () => db.Services.ExpressionRegistry.RegisterKeyword("AFTER_INIT");

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void WithPassword_should_configure_encryption_for_connection_strings()
        {
            using var file = new TempFile();

            using (var db = new LiteDatabaseBuilder()
                .UseFile(file.Filename)
                .WithPassword("secret")
                .Build())
            {
                db.GetCollection<BsonDocument>("docs").Insert(new BsonDocument { ["_id"] = 1 });
            }

            using (var reopened = new LiteDatabase($"Filename={file.Filename};password=secret"))
            {
                reopened.GetCollection<BsonDocument>("docs").Count().Should().Be(1);
            }

            Action wrongPassword = () =>
            {
                using var db = new LiteDatabase($"Filename={file.Filename};password=wrong");
                db.GetCollection<BsonDocument>("docs").Count();
            };

            wrongPassword.Should().Throw<LiteException>();
        }

        [Fact]
        public void AsReadOnly_should_prevent_writes()
        {
            using var file = new TempFile();

            using (var db = new LiteDatabaseBuilder()
                .UseFile(file.Filename)
                .Build())
            {
                db.GetCollection<BsonDocument>("docs").Insert(new BsonDocument { ["_id"] = 1 });
            }

            using var readOnly = new LiteDatabaseBuilder()
                .UseFile(file.Filename)
                .AsReadOnly()
                .Build();

            Action write = () => readOnly.GetCollection<BsonDocument>("docs").Insert(new BsonDocument { ["_id"] = 2 });

            write.Should().Throw<LiteException>();
        }

        [Fact]
        public void WithConnectionType_should_throw_when_used_with_UseEngine()
        {
            using var engine = new LiteEngine(new EngineSettings { Filename = ":memory:" });

            var builder = new LiteDatabaseBuilder()
                .WithConnectionType(ConnectionType.Shared);

            Action act = () => builder.UseEngine(engine, ownsEngine: false);

            act.Should().Throw<InvalidOperationException>();
        }

        private sealed class DisposableTrackingPlugin : ILitePlugin, IDisposable
        {
            public bool Initialized { get; private set; }

            public bool Disposed { get; private set; }

            public void Initialize(LiteDatabase database, ILitePluginContext context)
            {
                Initialized = true;
            }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new List<(LogLevel, string)>();

            public void Write(LogLevel level, string message, Exception exception = null)
            {
                Entries.Add((level, message));
            }
        }
    }
}
