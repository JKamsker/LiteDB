using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FluentAssertions;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Tests.Utils;
using LiteDB.Vector;
using LiteDB.Vector.Document;
using Xunit;
using static LiteDB.Constants;

namespace LiteDB.Tests.Engine
{
    public class PluginRebuildSafety_Tests
    {
        [Fact]
        public void Rebuild_should_refuse_when_plugin_indexes_exist_but_plugin_is_missing()
        {
            using var file = new TempFile();

            using (var pluginDatabase = new LiteDatabase(file.Filename, plugins: new[] { VectorSearchPlugin.Instance }))
            {
                var collection = pluginDatabase.GetCollection<TestDocument>("docs");
                collection.Insert(new TestDocument { Id = 1, Embedding = new[] { 0.1f, 0.2f, 0.3f } });
                collection.EnsureIndex("embedding_idx", x => x.Embedding, new VectorIndexOptions(3)).Should().BeTrue();
                pluginDatabase.Checkpoint();
            }

            using var vanillaDatabase = new LiteDatabase(file.Filename, new LiteDatabaseOptions
            {
                MissingPluginBehavior = PluginMissingBehavior.AllowIfSafe
            });

            Action act = () => vanillaDatabase.Rebuild();

            act.Should().Throw<LiteException>()
                .Where(ex => ex.ErrorCode == LiteException.PLUGIN_REQUIRED);
        }

        [Fact]
        public void FileReaderV8_Open_should_not_swallow_plugin_required()
        {
            using var file = new TempFile();

            using (var pluginDatabase = new LiteDatabase(file.Filename, plugins: new[] { VectorSearchPlugin.Instance }))
            {
                var collection = pluginDatabase.GetCollection<TestDocument>("docs");
                collection.Insert(new TestDocument { Id = 1, Embedding = new[] { 0.1f, 0.2f, 0.3f } });
                collection.EnsureIndex("embedding_idx", x => x.Embedding, new VectorIndexOptions(3)).Should().BeTrue();
                pluginDatabase.Checkpoint();
            }

            var logFile = FileHelper.GetLogFile(file.Filename);
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }

            var collectionPageId = ReadCollectionPageId(file.Filename, "docs");
            collectionPageId.Should().NotBe(uint.MaxValue);
            ReadPageType(file.Filename, collectionPageId).Should().Be(PageType.Collection);
            var markerOffset = FindPluginMetadataMarkerOffset(file.Filename, collectionPageId, "embedding_idx");
            (ReadByteAt(file.Filename, collectionPageId, markerOffset) & 0x80).Should().Be(0x80);
            FlipPluginMetadataMarkerHighBit(file.Filename, collectionPageId, markerOffset);
            (ReadByteAt(file.Filename, collectionPageId, markerOffset) & 0x80).Should().Be(0);
            AssertLegacyMetadataMarkerThrows(file.Filename, collectionPageId);

            var settings = new EngineSettings { Filename = file.Filename };
            var errors = new List<FileReaderError>();

            using var reader = new FileReaderV8(settings, errors, plugins: null);

            Action act = () => reader.Open();

            act.Should().Throw<LiteException>()
                .Where(ex => ex.ErrorCode == LiteException.PLUGIN_REQUIRED);
        }

        private static uint ReadCollectionPageId(string filename, string collectionName)
        {
            var headerBytes = new byte[PAGE_SIZE];

            using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.ReadExactly(headerBytes);
            }

            var headerBuffer = new PageBuffer(headerBytes, 0, uniqueID: 0);
            var header = new HeaderPage(headerBuffer);

            return header.GetCollectionPageID(collectionName);
        }

        private static int FindPluginMetadataMarkerOffset(string filename, uint collectionPageId, string indexName)
        {
            var pageBytes = new byte[PAGE_SIZE];

            using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.Position = BasePage.GetPagePosition(collectionPageId);
                stream.ReadExactly(pageBytes);
            }

            var expectedPluginId = VectorPlugin.PluginId;
            var pageBuffer = new PageBuffer(pageBytes, 0, uniqueID: 0);
            var area = pageBuffer.Slice(PAGE_HEADER_SIZE, PAGE_SIZE - PAGE_HEADER_SIZE);

            using (var reader = new BufferReader(new[] { area }, false))
            {
                for (var i = 0; i < PAGE_FREE_LIST_SLOTS; i++)
                {
                    reader.ReadUInt32();
                }

                reader.Skip(CollectionPage.P_INDEXES - PAGE_HEADER_SIZE - reader.Position);

                var indexCount = reader.ReadByte();
                for (var i = 0; i < indexCount; i++)
                {
                    _ = new CollectionIndex(reader);
                }

                var metadataCount = reader.ReadByte();
                for (var i = 0; i < metadataCount; i++)
                {
                    var name = reader.ReadCString();
                    var markerOffset = PAGE_HEADER_SIZE + reader.Position;
                    var marker = reader.ReadByte();

                    if ((marker & 0x80) == 0)
                    {
                        continue;
                    }

                    var pluginIdLength = marker & 0x7F;
                    var pluginId = reader.ReadString(pluginIdLength);
                    var payloadLength = reader.ReadUInt16();
                    reader.Skip(payloadLength);

                    if (string.Equals(name, indexName, StringComparison.Ordinal) &&
                        string.Equals(pluginId, expectedPluginId, StringComparison.Ordinal))
                    {
                        return markerOffset;
                    }
                }
            }

            throw new InvalidOperationException($"Unable to locate plugin metadata marker for index '{indexName}'.");
        }

        private static void FlipPluginMetadataMarkerHighBit(string filename, uint collectionPageId, int markerOffset)
        {
            using var stream = new FileStream(filename, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            stream.Position = BasePage.GetPagePosition(collectionPageId) + markerOffset;

            var marker = stream.ReadByte();
            if (marker < 0)
            {
                throw new EndOfStreamException("Unable to read plugin metadata marker.");
            }

            var legacyMarker = (byte)(marker & 0x7F);

            stream.Position = BasePage.GetPagePosition(collectionPageId) + markerOffset;
            stream.WriteByte(legacyMarker);
            stream.Flush();
        }

        private static PageType ReadPageType(string filename, uint pageId)
        {
            using var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Position = BasePage.GetPagePosition(pageId) + BasePage.P_PAGE_TYPE;

            var value = stream.ReadByte();
            if (value < 0)
            {
                throw new EndOfStreamException("Unable to read page type.");
            }

            return (PageType)(byte)value;
        }

        private static void AssertLegacyMetadataMarkerThrows(string filename, uint collectionPageId)
        {
            var pageBytes = new byte[PAGE_SIZE];

            using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.Position = BasePage.GetPagePosition(collectionPageId);
                stream.ReadExactly(pageBytes);
            }

            var pageBuffer = new PageBuffer(pageBytes, 0, uniqueID: 0);

            Action act = () => new CollectionPage(pageBuffer);

            act.Should().Throw<LiteException>()
                .Where(ex => ex.ErrorCode == LiteException.PLUGIN_REQUIRED);
        }

        private static byte ReadByteAt(string filename, uint collectionPageId, int markerOffset)
        {
            using var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Position = BasePage.GetPagePosition(collectionPageId) + markerOffset;

            var marker = stream.ReadByte();
            if (marker < 0)
            {
                throw new EndOfStreamException("Unable to read plugin metadata marker.");
            }

            return (byte)marker;
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; }
        }
    }
}
