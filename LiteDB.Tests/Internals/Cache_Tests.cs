using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Internals
{
    public class Cache_Tests
    {
        [Fact]
        public void Cache_Read_Write()
        {
            var m = new MemoryCache(new int[] { 10 });

            m.PagesInUse.Should().Be(0);

            var p0 = m.NewPage();

            // new pages are writable
            (p0.ShareCounter).Should().Be(-1);

            // simulate write operation on page
            p0.Origin = FileOrigin.Log;
            p0.Position = 0;
            p0.Write(123, 10);

            m.WritablePages.Should().Be(1);

            var readable = m.TryMoveToReadable(p0);

            // now, page are readable
            readable.Should().BeTrue();
            p0.ShareCounter.Should().Be(0);

            // now get same page again
            var p1 = m.GetReadablePage(0, FileOrigin.Log, (p, s) => { });

            p1.ReadInt32(10).Should().Be(123);
            p1.ShareCounter.Should().Be(1);

            // let's read again (must return same instance but increment share counter)
            var p2 = m.GetReadablePage(0, FileOrigin.Log, (p, s) => { });

            p1.Should().Be(p2);

            p2.ShareCounter.Should().Be(2);

            // releasing first
            p1.Release();

            p2.ShareCounter.Should().Be(1);

            // releasing second
            p2.Release();

            m.PagesInUse.Should().Be(0);

            p0.ShareCounter.Should().Be(0);
            p1.ShareCounter.Should().Be(0);
            p2.ShareCounter.Should().Be(0);
        }

        [Fact]
        public void Cache_Extends()
        {
            var m = new MemoryCache(new int[] { 10 });
            var pos = 0;

            // in ctor, memory cache create only 1 memory segment
            m.ExtendSegments.Should().Be(1);

            var pages = new List<PageBuffer>();

            // request 17 pages to write in disk
            for (var i = 0; i < 17; i++)
            {
                pages.Add(m.NewPage());
            }

            m.ExtendSegments.Should().Be(2);

            // release 5 pages (this pages will be in readable-list)
            foreach (var p in pages.Take(5))
            {
                // simulate write
                p.Origin = FileOrigin.Log;
                p.Position = ++pos;

                m.TryMoveToReadable(p);
            }

            // checks if still 2 segments in memory (segments never decrease)
            m.ExtendSegments.Should().Be(2);

            // but only 3 free pages
            m.FreePages.Should().Be(3);

            // now, request new 5 pages to write
            for (var i = 0; i < 5; i++)
            {
                pages.Add(m.NewPage());
            }

            // extends must be increase
            m.ExtendSegments.Should().Be(3);

            // but if I release more than 10 pages, now I will re-use old pages
            foreach (var p in pages.Where(x => x.ShareCounter == -1).Take(10))
            {
                // simulate write
                p.Origin = FileOrigin.Log;
                p.Position = ++pos;

                m.TryMoveToReadable(p);
            }

            m.WritablePages.Should().Be(7);
            m.FreePages.Should().Be(8);

            // now, if I request for 10 pages, all pages will be reused (no segment extend)
            for (var i = 0; i < 10; i++)
            {
                pages.Add(m.NewPage());
            }

            // keep same extends
            m.ExtendSegments.Should().Be(3);

            // discard all pages
            PageBuffer pw;

            while ((pw = pages.FirstOrDefault(x => x.ShareCounter == -1)) != null)
            {
                m.DiscardPage(pw);
            }
        }

        [Fact]
        public void Cache_ReadablePage_FactoryException_DoesNotLeakPages()
        {
            var m = new MemoryCache(new int[] { 1 });
            var initialFreePages = m.FreePages;

            m.Invoking(cache => cache.GetReadablePage(0, FileOrigin.Data, (_, __) => throw new InvalidOperationException("boom")))
                .Should()
                .Throw<InvalidOperationException>();

            m.FreePages.Should().Be(initialFreePages);
            m.GetPages().Should().BeEmpty();
        }

        [Fact]
        public void Cache_WritablePage_FactoryException_DoesNotLeakPages()
        {
            var m = new MemoryCache(new int[] { 1 });
            var initialFreePages = m.FreePages;

            m.Invoking(cache => cache.GetWritablePage(0, FileOrigin.Data, (_, __) => throw new InvalidOperationException("boom")))
                .Should()
                .Throw<InvalidOperationException>();

            m.FreePages.Should().Be(initialFreePages);
        }

        [Fact]
        public void Cache_ReadablePage_LostRace_DoesNotReturnDirtyTimestampZeroPages()
        {
            var m = new MemoryCache(new int[] { 1 });

            var page = m.GetReadablePage(0, FileOrigin.Data, (pos, slice) =>
            {
                slice[0] = 123;

                var competing = m.NewPage();
                competing.Position = pos;
                competing.Origin = FileOrigin.Data;
                competing[0] = 55;

                m.TryMoveToReadable(competing).Should().BeTrue();
            });

            page[0].Should().Be(55);
            page.Release();

            var writable = m.NewPage();

            writable.All(0).Should().BeTrue("lost readable pages must be cleared when returned to the free list");

            m.DiscardPage(writable);
        }

        [Fact]
        public void Cache_ReadablePage_Should_Not_Corrupt_Under_Extend_Races()
        {
            var m = new MemoryCache(new int[] { 1 });
            const int positions = 32;
            const int iterations = 2000;

            var expected = new byte[positions][];

            for (var i = 0; i < positions; i++)
            {
                var buffer = new byte[Constants.PAGE_SIZE];
                for (var j = 0; j < buffer.Length; j++)
                {
                    buffer[j] = (byte)(i ^ 0x5A);
                }

                expected[i] = buffer;

                var page = m.GetReadablePage(i, FileOrigin.Data, (pos, slice) =>
                {
                    Buffer.BlockCopy(expected[(int)pos], 0, slice.Array, slice.Offset, Constants.PAGE_SIZE);
                });

                page.Release();
            }

            var errors = new ConcurrentQueue<Exception>();

            var reader = Task.Run(() =>
            {
                try
                {
                    for (var i = 0; i < iterations; i++)
                    {
                        var pos = i % positions;
                        var page = m.GetReadablePage(pos, FileOrigin.Data, (p, slice) =>
                        {
                            Buffer.BlockCopy(expected[(int)p], 0, slice.Array, slice.Offset, Constants.PAGE_SIZE);
                        });

                        page[0].Should().Be(expected[pos][0]);
                        page[1234].Should().Be(expected[pos][1234]);
                        page[Constants.PAGE_SIZE - 1].Should().Be(expected[pos][Constants.PAGE_SIZE - 1]);

                        page.Release();
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            });

            var evictor = Task.Run(() =>
            {
                try
                {
                    for (var i = 0; i < iterations; i++)
                    {
                        var pages = new List<PageBuffer>();

                        for (var j = 0; j < 4; j++)
                        {
                            pages.Add(m.NewPage());
                        }

                        foreach (var page in pages)
                        {
                            m.DiscardPage(page);
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            });

            var copier = Task.Run(() =>
            {
                try
                {
                    for (var i = 0; i < iterations; i++)
                    {
                        var pos = i % positions;
                        var writable = m.GetWritablePage(pos, FileOrigin.Data, (p, slice) =>
                        {
                            Buffer.BlockCopy(expected[(int)p], 0, slice.Array, slice.Offset, Constants.PAGE_SIZE);
                        });

                        writable[0].Should().Be(expected[pos][0]);
                        writable[1234].Should().Be(expected[pos][1234]);
                        writable[Constants.PAGE_SIZE - 1].Should().Be(expected[pos][Constants.PAGE_SIZE - 1]);

                        m.DiscardPage(writable);
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            });

            Task.WaitAll(reader, evictor, copier);

            errors.Should().BeEmpty();
        }

        [Fact]
        public void Cache_UniqueIDNumbering()
        {
            // Test case when second segment size is smaller than first
            int[] segmentSizes = { 5, 3 };
            ConsumeNewPages(segmentSizes);

            // Test default database segment sizes
            segmentSizes = Constants.MEMORY_SEGMENT_SIZES;
            ConsumeNewPages(segmentSizes);

            // Test random memory segment sizes
            Random rnd = new Random(DateTime.Now.Millisecond);
            segmentSizes = new int[rnd.Next(3, 12)];
            for (int i = 0; i < segmentSizes.Length; i++)
            {
                segmentSizes[i] = rnd.Next(1, 1000);
            }
            ConsumeNewPages(segmentSizes);
        }

        private void ConsumeNewPages(int[] segmentSizes)
        {
            var m = new MemoryCache(segmentSizes);

            // Test some additional segments using last segment size more than once
            var totalSegments = segmentSizes.Sum() + 10;
            for (int i = 1; i <= totalSegments; i++)
            {
                PageBuffer p = m.NewPage();
                p.UniqueID.Should().Be(i);

                // Set ShareCounter to 0 to proper disposal (not needed in this test)
                p.ShareCounter = 0;
            }
        }
    }
}
