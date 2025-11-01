extern alias LiteDbBase;

using System;
using System.Linq.Expressions;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial
{
    /// <summary>
    /// Provides spatial helper methods that compose LINQ predicates routed through the spatial plugin.
    /// </summary>
    public static class SpatialQueryableExtensions
    {
        /// <summary>
        /// Filters the query to points near the specified center.
        /// </summary>
        /// <param name="source">The queryable collection.</param>
        /// <param name="geometry">Selector that identifies the spatial field.</param>
        /// <param name="center">The center point.</param>
        /// <param name="radius">The search radius expressed in meters.</param>
        /// <param name="formula">Optional distance formula override.</param>
        public static BaseLiteDB.ILiteQueryable<T> WhereNear<T>(
            this BaseLiteDB.ILiteQueryable<T> source,
            Expression<Func<T, GeoPoint>> geometry,
            GeoPoint center,
            double radius,
            GeographicDistanceMode? distanceMode = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            var predicate = BuildNearPredicate(geometry, center, radius, distanceMode);
            return source.Where(predicate);
        }

        /// <summary>
        /// Filters the query to points near the specified center.
        /// </summary>
        /// <param name="source">The queryable collection.</param>
        /// <param name="geometry">Selector that identifies the nullable spatial field.</param>
        /// <param name="center">The center point.</param>
        /// <param name="radius">The search radius expressed in meters.</param>
        /// <param name="distanceMode">Optional distance formula override.</param>
        public static BaseLiteDB.ILiteQueryable<T> WhereNear<T>(
            this BaseLiteDB.ILiteQueryable<T> source,
            Expression<Func<T, GeoPoint?>> geometry,
            GeoPoint center,
            double radius,
            GeographicDistanceMode? distanceMode = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            var predicate = BuildNearPredicate(geometry, (GeoPoint?)center, radius, distanceMode);
            return source.Where(predicate);
        }

        /// <summary>
        /// Filters the query to three-dimensional points near the specified center.
        /// </summary>
        public static BaseLiteDB.ILiteQueryable<T> WhereNear<T>(
            this BaseLiteDB.ILiteQueryable<T> source,
            Expression<Func<T, GeoPoint3D>> geometry,
            GeoPoint3D center,
            double radius)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            var predicate = BuildNearPredicate(geometry, center, radius, distanceMode: null);
            return source.Where(predicate);
        }

        /// <summary>
        /// Filters the query to three-dimensional points near the specified center.
        /// </summary>
        public static BaseLiteDB.ILiteQueryable<T> WhereNear<T>(
            this BaseLiteDB.ILiteQueryable<T> source,
            Expression<Func<T, GeoPoint3D?>> geometry,
            GeoPoint3D center,
            double radius)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            var predicate = BuildNearPredicate(geometry, (GeoPoint3D?)center, radius, distanceMode: null);
            return source.Where(predicate);
        }

        /// <summary>
        /// Filters the query to points near the specified center using a document field path.
        /// </summary>
        public static BaseLiteDB.ILiteQueryable<T> WhereNear<T>(
            this BaseLiteDB.ILiteQueryable<T> source,
            string geometryField,
            GeoPoint center,
            double radius,
            GeographicDistanceMode? distanceMode = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(geometryField)) throw new ArgumentException("Geometry field must be provided.", nameof(geometryField));

            EnsureNonNegativeRadius(radius);
            var fieldReference = BuildFieldReference(geometryField);
            var predicate = CreateNearExpression(fieldReference, CreateGeoPointValue(center), radius, distanceMode);
            return source.Where(predicate);
        }

        /// <summary>
        /// Filters the query to three-dimensional points near the specified center using a document field path.
        /// </summary>
        public static BaseLiteDB.ILiteQueryable<T> WhereNear<T>(
            this BaseLiteDB.ILiteQueryable<T> source,
            string geometryField,
            GeoPoint3D center,
            double radius)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(geometryField)) throw new ArgumentException("Geometry field must be provided.", nameof(geometryField));

            EnsureNonNegativeRadius(radius);
            var fieldReference = BuildFieldReference(geometryField);
            var predicate = CreateNearExpression(fieldReference, CreateGeoPoint3DValue(center), radius, distanceMode: null);
            return source.Where(predicate);
        }

        /// <summary>
        /// Filters the query to points near the specified center using a custom geometry expression.
        /// </summary>
        public static BaseLiteDB.ILiteQueryable<T> WhereNear<T>(
            this BaseLiteDB.ILiteQueryable<T> source,
            BaseLiteDB.BsonExpression geometryExpression,
            GeoPoint center,
            double radius,
            GeographicDistanceMode? distanceMode = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (geometryExpression == null) throw new ArgumentNullException(nameof(geometryExpression));

            EnsureNonNegativeRadius(radius);
            var predicate = CreateNearExpression(ExtractExpressionSource(geometryExpression), CreateGeoPointValue(center), radius, distanceMode);
            return source.Where(predicate);
        }

        /// <summary>
        /// Filters the query to three-dimensional points near the specified center using a custom geometry expression.
        /// </summary>
        public static BaseLiteDB.ILiteQueryable<T> WhereNear<T>(
            this BaseLiteDB.ILiteQueryable<T> source,
            BaseLiteDB.BsonExpression geometryExpression,
            GeoPoint3D center,
            double radius)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (geometryExpression == null) throw new ArgumentNullException(nameof(geometryExpression));

            EnsureNonNegativeRadius(radius);
            var predicate = CreateNearExpression(ExtractExpressionSource(geometryExpression), CreateGeoPoint3DValue(center), radius, distanceMode: null);
            return source.Where(predicate);
        }

        /// <summary>
        /// Filters the query to points contained within the provided bounding box.
        /// </summary>
        public static BaseLiteDB.ILiteQueryable<T> WhereWithinBox<T>(
            this BaseLiteDB.ILiteQueryable<T> source,
            Expression<Func<T, GeoPoint>> geometry,
            BoundingBox bounds)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (bounds.Dimensions != 2)
            {
                throw new ArgumentException("Two-dimensional bounding boxes are required.", nameof(bounds));
            }

            var predicate = BuildWithinPredicate(geometry, bounds);
            return source.Where(predicate);
        }

        /// <summary>
        /// Filters the query to points contained within the provided bounding box.
        /// </summary>
        public static BaseLiteDB.ILiteQueryable<T> WhereWithinBox<T>(
            this BaseLiteDB.ILiteQueryable<T> source,
            Expression<Func<T, GeoPoint?>> geometry,
            BoundingBox bounds)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (bounds.Dimensions != 2)
            {
                throw new ArgumentException("Two-dimensional bounding boxes are required.", nameof(bounds));
            }

            var predicate = BuildWithinPredicate(geometry, bounds);
            return source.Where(predicate);
        }

        /// <summary>
        /// Filters the query to points contained within the provided bounding box using a document field path.
        /// </summary>
        public static BaseLiteDB.ILiteQueryable<T> WhereWithinBox<T>(
            this BaseLiteDB.ILiteQueryable<T> source,
            string geometryField,
            BoundingBox bounds)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(geometryField)) throw new ArgumentException("Geometry field must be provided.", nameof(geometryField));
            if (bounds.Dimensions != 2 && bounds.Dimensions != 3)
            {
                throw new ArgumentException("Bounding boxes must describe two or three dimensions.", nameof(bounds));
            }

            var predicate = CreateInBoxExpression(BuildFieldReference(geometryField), CreateBoundingBoxValue(bounds));
            return source.Where(predicate);
        }

        /// <summary>
        /// Filters the query to points contained within the provided bounding box using a custom geometry expression.
        /// </summary>
        public static BaseLiteDB.ILiteQueryable<T> WhereWithinBox<T>(
            this BaseLiteDB.ILiteQueryable<T> source,
            BaseLiteDB.BsonExpression geometryExpression,
            BoundingBox bounds)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (geometryExpression == null) throw new ArgumentNullException(nameof(geometryExpression));
            if (bounds.Dimensions != 2 && bounds.Dimensions != 3)
            {
                throw new ArgumentException("Bounding boxes must describe two or three dimensions.", nameof(bounds));
            }

            var predicate = CreateInBoxExpression(ExtractExpressionSource(geometryExpression), CreateBoundingBoxValue(bounds));
            return source.Where(predicate);
        }

        /// <summary>
        /// Filters the query to three-dimensional points contained within the provided bounding box.
        /// </summary>
        public static BaseLiteDB.ILiteQueryable<T> WhereWithinBox<T>(
            this BaseLiteDB.ILiteQueryable<T> source,
            Expression<Func<T, GeoPoint3D>> geometry,
            BoundingBox bounds)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (bounds.Dimensions != 3)
            {
                throw new ArgumentException("Three-dimensional bounding boxes are required.", nameof(bounds));
            }

            var predicate = BuildWithinPredicate(geometry, bounds);
            return source.Where(predicate);
        }

        /// <summary>
        /// Filters the query to three-dimensional points contained within the provided bounding box.
        /// </summary>
        public static BaseLiteDB.ILiteQueryable<T> WhereWithinBox<T>(
            this BaseLiteDB.ILiteQueryable<T> source,
            Expression<Func<T, GeoPoint3D?>> geometry,
            BoundingBox bounds)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (bounds.Dimensions != 3)
            {
                throw new ArgumentException("Three-dimensional bounding boxes are required.", nameof(bounds));
            }

            var predicate = BuildWithinPredicate(geometry, bounds);
            return source.Where(predicate);
        }

        private static Expression<Func<T, bool>> BuildNearPredicate<T, TPoint>(
            Expression<Func<T, TPoint>> geometry,
            TPoint center,
            double radius,
            GeographicDistanceMode? distanceMode)
        {
            var parameter = Expression.Parameter(typeof(T), "document");
            var rewritten = ReplaceParameter(geometry, parameter);

            var sourceType = typeof(TPoint);
            var underlyingType = Nullable.GetUnderlyingType(sourceType);
            var spatialType = underlyingType ?? sourceType;
            var candidate = underlyingType != null
                ? Expression.Convert(rewritten, spatialType)
                : rewritten;

            Expression call;
            if (spatialType == typeof(GeoPoint))
            {
                var arguments = new[]
                {
                    candidate,
                    Expression.Constant(NormalizeCenterValue(center, spatialType), typeof(GeoPoint)),
                    Expression.Constant(radius),
                    distanceMode.HasValue
                        ? Expression.Constant(distanceMode.Value, typeof(GeographicDistanceMode?))
                        : Expression.Constant(null, typeof(GeographicDistanceMode?))
                };

                call = Expression.Call(
                    typeof(SpatialExpressions),
                    nameof(SpatialExpressions.Near),
                    Type.EmptyTypes,
                    arguments);
            }
            else if (spatialType == typeof(GeoPoint3D))
            {
                var arguments = new Expression[]
                {
                    candidate,
                    Expression.Constant(NormalizeCenterValue(center, spatialType), typeof(GeoPoint3D)),
                    Expression.Constant(radius),
                    Expression.Constant(null, typeof(GeographicDistanceMode?))
                };

                call = Expression.Call(
                    typeof(SpatialExpressions),
                    nameof(SpatialExpressions.Near),
                    Type.EmptyTypes,
                    arguments);
            }
            else
            {
                throw new NotSupportedException($"Spatial near queries are not supported for geometry type '{spatialType.FullName}'.");
            }

            return Expression.Lambda<Func<T, bool>>(call, parameter);
        }

        private static Expression<Func<T, bool>> BuildWithinPredicate<T, TPoint>(
            Expression<Func<T, TPoint>> geometry,
            BoundingBox bounds)
        {
            var parameter = Expression.Parameter(typeof(T), "document");
            var rewritten = ReplaceParameter(geometry, parameter);

            var sourceType = typeof(TPoint);
            var underlyingType = Nullable.GetUnderlyingType(sourceType);
            var spatialType = underlyingType ?? sourceType;
            var candidate = underlyingType != null
                ? Expression.Convert(rewritten, spatialType)
                : rewritten;

            Expression call;
            if (spatialType == typeof(GeoPoint))
            {
                call = Expression.Call(
                    typeof(SpatialExpressions),
                    nameof(SpatialExpressions.InBox),
                    Type.EmptyTypes,
                    candidate,
                    Expression.Constant(bounds));
            }
            else if (spatialType == typeof(GeoPoint3D))
            {
                call = Expression.Call(
                    typeof(SpatialExpressions),
                    nameof(SpatialExpressions.InBox),
                    Type.EmptyTypes,
                    candidate,
                    Expression.Constant(bounds));
            }
            else
            {
                throw new NotSupportedException($"Spatial bounding box queries are not supported for geometry type '{spatialType.FullName}'.");
            }

            return Expression.Lambda<Func<T, bool>>(call, parameter);
        }

        private static Expression ReplaceParameter<T, TPoint>(Expression<Func<T, TPoint>> expression, ParameterExpression replacement)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));

            return new ParameterReplacementVisitor(expression.Parameters[0], replacement)
                .Visit(expression.Body);
        }

        private static BaseLiteDB.BsonExpression CreateNearExpression(
            string geometryReference,
            BaseLiteDB.BsonValue centerValue,
            double radius,
            GeographicDistanceMode? distanceMode)
        {
            if (string.IsNullOrWhiteSpace(geometryReference))
            {
                throw new ArgumentException("Geometry reference must be provided.", nameof(geometryReference));
            }

            var parameters = new BaseLiteDB.BsonDocument
            {
                ["0"] = centerValue ?? BaseLiteDB.BsonValue.Null,
                ["1"] = new BaseLiteDB.BsonValue(radius),
                ["2"] = CreateDistanceModeValue(distanceMode)
            };

            return BaseLiteDB.BsonExpression.Create($"SPATIAL_NEAR({geometryReference}, @0, @1, @2)", parameters);
        }

        private static BaseLiteDB.BsonExpression CreateInBoxExpression(string geometryReference, BaseLiteDB.BsonValue boundingBox)
        {
            if (string.IsNullOrWhiteSpace(geometryReference))
            {
                throw new ArgumentException("Geometry reference must be provided.", nameof(geometryReference));
            }

            var parameters = new BaseLiteDB.BsonDocument
            {
                ["0"] = boundingBox ?? BaseLiteDB.BsonValue.Null
            };

            return BaseLiteDB.BsonExpression.Create($"SPATIAL_IN_BOX({geometryReference}, @0)", parameters);
        }

        private static string BuildFieldReference(string geometryField)
        {
            var trimmed = geometryField?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                throw new ArgumentException("Geometry field must be provided.", nameof(geometryField));
            }

            if (trimmed.StartsWith("$." , StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(2);
            }
            else if (trimmed.StartsWith("$", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(1);
            }

            trimmed = trimmed.Replace("[*]", string.Empty);

            if (trimmed.Length == 0)
            {
                throw new ArgumentException("Geometry field must resolve to a document member.", nameof(geometryField));
            }

            return "$." + trimmed;
        }

        private static string ExtractExpressionSource(BaseLiteDB.BsonExpression expression)
        {
            var source = expression?.Source;
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ArgumentException("Geometry expression must expose a source string.", nameof(expression));
            }

            return source;
        }

        private static void EnsureNonNegativeRadius(double radius)
        {
            if (radius < 0d || double.IsNaN(radius) || double.IsInfinity(radius))
            {
                throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be a non-negative finite number.");
            }
        }

        private static BaseLiteDB.BsonValue CreateGeoPointValue(GeoPoint point)
        {
            var document = new BaseLiteDB.BsonDocument
            {
                ["Longitude"] = point.Longitude,
                ["Latitude"] = point.Latitude
            };

            return document;
        }

        private static BaseLiteDB.BsonValue CreateGeoPoint3DValue(GeoPoint3D point)
        {
            var document = new BaseLiteDB.BsonDocument
            {
                ["X"] = point.X,
                ["Y"] = point.Y,
                ["Z"] = point.Z
            };

            return document;
        }

        private static BaseLiteDB.BsonValue CreateBoundingBoxValue(BoundingBox box)
        {
            var values = box.ToArray();
            var array = new BaseLiteDB.BsonArray();

            foreach (var value in values)
            {
                array.Add(new BaseLiteDB.BsonValue(value));
            }

            return array;
        }

        private static BaseLiteDB.BsonValue CreateDistanceModeValue(GeographicDistanceMode? mode)
        {
            if (!mode.HasValue)
            {
                return BaseLiteDB.BsonValue.Null;
            }

            return new BaseLiteDB.BsonValue(mode.Value.ToString());
        }

        private static object NormalizeCenterValue<TPoint>(TPoint center, Type spatialType)
        {
            if (spatialType == null)
            {
                throw new ArgumentNullException(nameof(spatialType));
            }

            if (spatialType == typeof(GeoPoint))
            {
                if (center is GeoPoint value)
                {
                    return value;
                }

                throw new ArgumentException("A non-null GeoPoint center is required.", nameof(center));
            }

            if (spatialType == typeof(GeoPoint3D))
            {
                if (center is GeoPoint3D value3D)
                {
                    return value3D;
                }

                throw new ArgumentException("A non-null GeoPoint3D center is required.", nameof(center));
            }

            throw new NotSupportedException($"Unsupported spatial center type '{spatialType.FullName}'.");
        }

        private sealed class ParameterReplacementVisitor : ExpressionVisitor
        {
            private readonly ParameterExpression _source;
            private readonly Expression _target;

            public ParameterReplacementVisitor(ParameterExpression source, Expression target)
            {
                _source = source ?? throw new ArgumentNullException(nameof(source));
                _target = target ?? throw new ArgumentNullException(nameof(target));
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (node == _source)
                {
                    return _target;
                }

                return base.VisitParameter(node);
            }
        }
    }
}

