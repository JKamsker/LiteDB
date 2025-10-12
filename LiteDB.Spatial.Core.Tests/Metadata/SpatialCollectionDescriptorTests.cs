extern alias LiteDbBase;

using System;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;

using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Metadata;

public sealed class SpatialCollectionDescriptorTests
{
    [Fact]
    public void WithEngineFactoryMaterializesEngineOnce()
    {
        var options = new SpatialIndexOptions(precisionBits: 8);
        var descriptor = new SpatialCollectionDescriptor("points", TestEngine.EngineName, 2, "location", options, SpatialEngineSettings.Empty);
        var factoryInvocations = 0;
        var engine = new TestEngine(TestEngine.EngineName, 2, options);
        var descriptorWithFactory = descriptor.WithEngineFactory(() =>
        {
            factoryInvocations++;
            return engine;
        });

        descriptorWithFactory.HasEngine.Should().BeFalse();
        descriptorWithFactory.HasEngineFactory.Should().BeTrue();

        descriptorWithFactory.TryGetEngine(out var realized).Should().BeTrue();
        realized.Should().BeSameAs(engine);
        factoryInvocations.Should().Be(1);
        descriptorWithFactory.HasEngine.Should().BeTrue();

        descriptorWithFactory.TryGetEngine(out var second).Should().BeTrue();
        second.Should().BeSameAs(engine);
        factoryInvocations.Should().Be(1);
    }

    [Fact]
    public void WithEngineThrowsWhenOptionsDiffer()
    {
        var descriptor = new SpatialCollectionDescriptor("points", TestEngine.EngineName, 2, "location", new SpatialIndexOptions(precisionBits: 8), SpatialEngineSettings.Empty);
        var mismatched = new TestEngine(TestEngine.EngineName, 2, new SpatialIndexOptions(precisionBits: 6));

        Action act = () => descriptor.WithEngine(mismatched);

        act.Should().Throw<ArgumentException>().WithMessage("*options*");
    }

    [Fact]
    public void TryGetEngineWrapsFactoryFailures()
    {
        var descriptor = new SpatialCollectionDescriptor("points", TestEngine.EngineName, 2, "location", new SpatialIndexOptions(precisionBits: 8), SpatialEngineSettings.Empty);
        var descriptorWithFactory = descriptor.WithEngineFactory(() => new TestEngine("OtherEngine", 2, descriptor.Options));

        Action act = () => descriptorWithFactory.TryGetEngine(out _);

        act.Should().Throw<SpatialMetadataException>().WithMessage("*materialize runtime engine*");
    }

    private sealed class TestEngine : ISpatialEngine
    {
        public const string EngineName = "DescriptorStub";

        private readonly int _dimensions;
        private readonly string _name;

        public TestEngine(string name, int dimensions, SpatialIndexOptions options)
        {
            _name = name;
            _dimensions = dimensions;
            Options = options;
            IndexEncoder = new MortonIndexEncoder(dimensions, options.PrecisionBits);
            Mapper = new NullMapper();
            Distance = new NullDistance();
        }

        public string Name => _name;
        public int Dimensions => _dimensions;
        public SpatialIndexOptions Options { get; }
        public ISpatialIndexEncoder IndexEncoder { get; }
        public ISpatialMapper Mapper { get; }
        public ISpatialDistance Distance { get; }

        public ISpatialQueryPlan PlanNear(GeoPoint center, double radius)
        {
            return new SpatialQueryPlan(Name, Dimensions, BoundingBox.From2D(center.Longitude, center.Latitude, center.Longitude, center.Latitude), CreateEmptyCovering(), $"Radius <= {radius}");
        }

        public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
        {
            return new SpatialQueryPlan(Name, Dimensions, BoundingBox.From3D(center.X, center.Y, center.Z, center.X, center.Y, center.Z), CreateEmptyCovering(), $"Radius <= {radius}");
        }

        public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
        {
            return new SpatialQueryPlan(Name, Dimensions, bounds, CreateEmptyCovering(), "Within bounds");
        }

        private static SpatialCoveringResult CreateEmptyCovering()
        {
            return new SpatialCoveringResult(Array.Empty<SpatialIndexRange>(), 0, 1, 0);
        }

        private sealed class NullMapper : ISpatialMapper
        {
            public BoundingBox GetBoundingBox(GeoPoint point) => BoundingBox.From2D(point.Longitude, point.Latitude, point.Longitude, point.Latitude);
            public BoundingBox GetBoundingBox(GeoPoint3D point) => BoundingBox.From3D(point.X, point.Y, point.Z, point.X, point.Y, point.Z);
            public ulong Encode(GeoPoint point) => 0;
            public ulong Encode(GeoPoint3D point) => 0;
            public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint point)
            {
                point = default;
                return false;
            }

            public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point)
            {
                point = default;
                return false;
            }
        }

        private sealed class NullDistance : ISpatialDistance
        {
            public double Distance(GeoPoint left, GeoPoint right) => 0d;
            public double Distance(GeoPoint3D left, GeoPoint3D right) => 0d;
        }
    }
}
