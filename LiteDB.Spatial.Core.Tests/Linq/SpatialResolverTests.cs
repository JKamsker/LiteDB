extern alias LiteDbBase;

using System;
using System.Linq.Expressions;
using FluentAssertions;
using Xunit;
using LiteDB.Spatial;

namespace LiteDB.Spatial.Core.Tests.Linq;

public sealed class SpatialResolverTests
{
    [Fact]
    public void TryResolveNear2DUsesDescriptorEngine()
    {
        var descriptor = new SpatialCollectionDescriptor("points", StubEngine.EngineName2D, 2, "location", new SpatialIndexOptions(precisionBits: 4));
        var engine = StubEngine.Create2D(descriptor.Options);
        var descriptorWithEngine = descriptor.WithEngine(engine);
        var resolver = new SpatialResolver();
        resolver.RegisterDescriptor(descriptorWithEngine, nameof(TestDocument.Location));

        Expression<Func<TestDocument, bool>> predicate = doc => SpatialExpressions.Near(doc.Location, new GeoPoint(1.5, -2.5), 10);
        var methodCall = (MethodCallExpression)predicate.Body;

        resolver.TryResolve(methodCall, out var plan).Should().BeTrue();

        engine.Last2DCenter.Should().Be(new GeoPoint(1.5, -2.5));
        engine.LastRadius.Should().Be(10);
        plan.Should().NotBeNull();
        plan!.EngineName.Should().Be(engine.Name);
    }

    [Fact]
    public void TryResolveNear2DMaterializesEngineFactory()
    {
        var descriptor = new SpatialCollectionDescriptor("points", StubEngine.EngineName2D, 2, "location", new SpatialIndexOptions(precisionBits: 4));
        var engine = StubEngine.Create2D(descriptor.Options);
        var descriptorWithFactory = descriptor.WithEngineFactory(() => engine);
        var resolver = new SpatialResolver();
        resolver.RegisterDescriptor(descriptorWithFactory, nameof(TestDocument.Location));

        Expression<Func<TestDocument, bool>> predicate = doc => SpatialExpressions.Near(doc.Location, new GeoPoint(2, -3), 7);
        var methodCall = (MethodCallExpression)predicate.Body;

        resolver.TryResolve(methodCall, out var plan).Should().BeTrue();

        engine.Last2DCenter.Should().Be(new GeoPoint(2, -3));
        engine.LastRadius.Should().Be(7);
        plan.Should().NotBeNull();
        plan!.EngineName.Should().Be(engine.Name);
        descriptorWithFactory.HasEngine.Should().BeTrue();
    }

    [Fact]
    public void TryResolveNear3DUsesDescriptorEngine()
    {
        var descriptor = new SpatialCollectionDescriptor("points3d", StubEngine.EngineName3D, 3, "position", new SpatialIndexOptions(precisionBits: 4));
        var engine = StubEngine.Create3D(descriptor.Options);
        var descriptorWithEngine = descriptor.WithEngine(engine);
        var resolver = new SpatialResolver();
        resolver.RegisterDescriptor(descriptorWithEngine, nameof(ThreeDimensionalDocument.Position));

        Expression<Func<ThreeDimensionalDocument, bool>> predicate = doc => SpatialExpressions.Near(doc.Position, new GeoPoint3D(1, 2, 3), 5);
        var methodCall = (MethodCallExpression)predicate.Body;

        resolver.TryResolve(methodCall, out var plan).Should().BeTrue();

        engine.Last3DCenter.Should().Be(new GeoPoint3D(1, 2, 3));
        engine.LastRadius.Should().Be(5);
        plan.Should().NotBeNull();
        plan!.EngineName.Should().Be(engine.Name);
        plan.Dimensions.Should().Be(3);
    }

    [Fact]
    public void TryResolveInBoxUsesDescriptorEngine()
    {
        var descriptor = new SpatialCollectionDescriptor("points", StubEngine.EngineName2D, 2, "location", new SpatialIndexOptions(precisionBits: 4));
        var engine = StubEngine.Create2D(descriptor.Options);
        var descriptorWithEngine = descriptor.WithEngine(engine);
        var resolver = new SpatialResolver();
        resolver.RegisterDescriptor(descriptorWithEngine, nameof(TestDocument.Location));

        var bounds = BoundingBox.From2D(0, 0, 10, 10);
        Expression<Func<TestDocument, bool>> predicate = doc => SpatialExpressions.InBox(doc.Location, bounds);
        var methodCall = (MethodCallExpression)predicate.Body;

        resolver.TryResolve(methodCall, out var plan).Should().BeTrue();

        engine.LastBounds.Should().Be(bounds);
        plan.Should().NotBeNull();
        plan!.CoveringBounds.Should().Be(bounds);
    }

    [Fact]
    public void TryResolveReturnsFalseForNonSpatialMethods()
    {
        var resolver = new SpatialResolver();
        var call = Expression.Call(typeof(string).GetMethod(nameof(string.IsNullOrEmpty), new[] { typeof(string) })!, Expression.Constant("test"));

        resolver.TryResolve(call, out var plan).Should().BeFalse();
        plan.Should().BeNull();
    }

    [Fact]
    public void TryResolveThrowsWhenDescriptorMissing()
    {
        var resolver = new SpatialResolver();
        Expression<Func<TestDocument, bool>> predicate = doc => SpatialExpressions.Near(doc.Location, new GeoPoint(0, 0), 1);
        var methodCall = (MethodCallExpression)predicate.Body;

        Action act = () => resolver.TryResolve(methodCall, out _);
        act.Should().Throw<SpatialMetadataException>().WithMessage("*member 'Location'*");
    }

    [Fact]
    public void TryResolveThrowsWhenEngineMissing()
    {
        var descriptor = new SpatialCollectionDescriptor("points", StubEngine.EngineName2D, 2, "location", new SpatialIndexOptions());
        var resolver = new SpatialResolver();
        resolver.RegisterDescriptor(descriptor, nameof(TestDocument.Location));

        Expression<Func<TestDocument, bool>> predicate = doc => SpatialExpressions.Near(doc.Location, new GeoPoint(0, 0), 1);
        var methodCall = (MethodCallExpression)predicate.Body;

        Action act = () => resolver.TryResolve(methodCall, out _);
        act.Should().Throw<SpatialMetadataException>().WithMessage("*runtime engine instance*");
    }

    [Fact]
    public void TryResolveThrowsOnDimensionMismatch()
    {
        var descriptor = new SpatialCollectionDescriptor("points", StubEngine.EngineName3D, 3, "location", new SpatialIndexOptions());
        var engine = StubEngine.Create3D(descriptor.Options);
        var descriptorWithEngine = descriptor.WithEngine(engine);
        var resolver = new SpatialResolver();
        resolver.RegisterDescriptor(descriptorWithEngine, nameof(TestDocument.Location));

        Expression<Func<TestDocument, bool>> predicate = doc => SpatialExpressions.Near(doc.Location, new GeoPoint(0, 0), 1);
        var methodCall = (MethodCallExpression)predicate.Body;

        Action act = () => resolver.TryResolve(methodCall, out _);
        act.Should().Throw<SpatialMetadataException>().WithMessage("*configured for 3D geometry*");
    }

    private sealed class TestDocument
    {
        public GeoPoint Location { get; set; }
    }

    private sealed class ThreeDimensionalDocument
    {
        public GeoPoint3D Position { get; set; }
    }

    private sealed class StubEngine : ISpatialEngine
    {
        public const string EngineName2D = "Stub2D";
        public const string EngineName3D = "Stub3D";

        private readonly int _dimensions;

        private StubEngine(int dimensions, SpatialIndexOptions options)
        {
            _dimensions = dimensions;
            Options = options;
            IndexEncoder = new MortonIndexEncoder(dimensions, options.PrecisionBits);
            Mapper = new StubMapper();
            Distance = new StubDistance();
        }

        public static StubEngine Create2D(SpatialIndexOptions options) => new StubEngine(2, options);
        public static StubEngine Create3D(SpatialIndexOptions options) => new StubEngine(3, options);

        public GeoPoint? Last2DCenter { get; private set; }
        public GeoPoint3D? Last3DCenter { get; private set; }
        public double? LastRadius { get; private set; }
        public BoundingBox? LastBounds { get; private set; }

        public string Name => _dimensions == 3 ? EngineName3D : EngineName2D;
        public int Dimensions => _dimensions;
        public SpatialIndexOptions Options { get; }
        public ISpatialIndexEncoder IndexEncoder { get; }
        public ISpatialMapper Mapper { get; }
        public ISpatialDistance Distance { get; }

        public ISpatialQueryPlan PlanNear(GeoPoint center, double radius)
        {
            if (_dimensions != 2)
            {
                throw new NotSupportedException("Only 2D centers are supported.");
            }

            Last2DCenter = center;
            LastRadius = radius;
            LastBounds = BoundingBox.From2D(center.Longitude - radius, center.Latitude - radius, center.Longitude + radius, center.Latitude + radius);
            return new SpatialQueryPlan(Name, Dimensions, LastBounds, Array.Empty<SpatialIndexRange>(), $"Radius <= {radius}", 0, false);
        }

        public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
        {
            if (_dimensions != 3)
            {
                throw new NotSupportedException("Only 3D centers are supported.");
            }

            Last3DCenter = center;
            LastRadius = radius;
            LastBounds = BoundingBox.From3D(center.X - radius, center.Y - radius, center.Z - radius, center.X + radius, center.Y + radius, center.Z + radius);
            return new SpatialQueryPlan(Name, Dimensions, LastBounds, Array.Empty<SpatialIndexRange>(), $"Radius <= {radius}", 0, false);
        }

        public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
        {
            LastBounds = bounds;
            return new SpatialQueryPlan(Name, Dimensions, bounds, Array.Empty<SpatialIndexRange>(), "Within bounds", 0, false);
        }

        private sealed class StubMapper : ISpatialMapper
        {
            public BoundingBox GetBoundingBox(GeoPoint point) => BoundingBox.From2D(point.Longitude, point.Latitude, point.Longitude, point.Latitude);
            public BoundingBox GetBoundingBox(GeoPoint3D point) => BoundingBox.From3D(point.X, point.Y, point.Z, point.X, point.Y, point.Z);
            public ulong Encode(GeoPoint point) => 0;
            public ulong Encode(GeoPoint3D point) => 0;
            public bool TryReadPoint(LiteDbBase::LiteDB.BsonDocument document, out GeoPoint point)
            {
                point = default;
                return false;
            }

            public bool TryReadPoint(LiteDbBase::LiteDB.BsonDocument document, out GeoPoint3D point)
            {
                point = default;
                return false;
            }
        }

        private sealed class StubDistance : ISpatialDistance
        {
            public double Distance(GeoPoint left, GeoPoint right) => 0d;
            public double Distance(GeoPoint3D left, GeoPoint3D right) => 0d;
        }
    }
}
