extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LiteDB.Spatial;
using BaseLiteDB = LiteDbBase::LiteDB;
using LiteDbEngine = LiteDbBase::LiteDB.Engine;
using LiteDbPlugins = LiteDbBase::LiteDB.Plugins;

namespace LiteDB.Spatial.Plugin.QueryPlanning
{
    internal sealed class SpatialQueryPlanningRule : LiteDbPlugins.IQueryPlanningRule
    {
        private readonly SpatialPluginServices _services;

        public SpatialQueryPlanningRule(SpatialPluginServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public bool TryRewrite(LiteDbPlugins.QueryPlanningContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var collection = context.Snapshot?.CollectionName;
            if (string.IsNullOrWhiteSpace(collection))
            {
                return false;
            }

            SpatialPredicate? predicate = null;
            foreach (var term in context.Terms)
            {
                if (SpatialPredicate.TryParse(term, out var parsed))
                {
                    predicate = parsed;
                    break;
                }
            }

            if (predicate == null)
            {
                return false;
            }

            if (!_services.TryResolveDescriptor(collection, predicate.GeometryField, out var descriptor))
            {
                if (!_services.TryGetDescriptor(collection, out descriptor) ||
                    descriptor == null ||
                    !string.Equals(descriptor.GeometryFieldName, predicate.GeometryField, StringComparison.OrdinalIgnoreCase))
                {
                    Log(LiteDbPlugins.LogLevel.Warning, $"No spatial metadata found for '{collection}.{predicate.GeometryField}'. Ensure EnsureIndex was executed with the spatial plugin enabled.");
                    return false;
                }
            }

            if (descriptor == null)
            {
                return false;
            }

            if (!TryBuildPlan(context, descriptor, predicate, out var index, out var indexExpression, out var additionalFilters))
            {
                Log(LiteDbPlugins.LogLevel.Debug, $"Unable to generate a spatial plan for '{collection}.{predicate.GeometryField}'. Falling back to default query processing.");
                return false;
            }

            context.UseIndex(
                index,
                indexExpression,
                consumedTerms: Array.Empty<BaseLiteDB.BsonExpression>(),
                isIndexKeyOnly: false,
                indexCost: null,
                additionalFilters: additionalFilters,
                replaceFilters: false);
            return true;
        }

        private void Log(LiteDbPlugins.LogLevel level, string message)
        {
            _services.Context?.Logger?.Write(level, $"[SpatialPlugin] {message}");
        }

        private bool TryBuildPlan(
            LiteDbPlugins.QueryPlanningContext context,
            SpatialCollectionDescriptor descriptor,
            SpatialPredicate predicate,
            out LiteDbEngine.Index index,
            out string indexExpression,
            out IReadOnlyList<BaseLiteDB.BsonExpression> additionalFilters)
        {
            index = default!;
            indexExpression = string.Empty;
            additionalFilters = Array.Empty<BaseLiteDB.BsonExpression>();

            if (descriptor == null)
            {
                return false;
            }

            var options = descriptor.Options;
            if (options == null)
            {
                return false;
            }

            var plan = predicate.Type switch
            {
                SpatialPredicateType.Near2D => BuildNear2DPlan(descriptor, predicate),
                SpatialPredicateType.Near3D => BuildNear3DPlan(descriptor, predicate),
                SpatialPredicateType.InBox2D => BuildWithinPlan(descriptor, predicate),
                SpatialPredicateType.InBox3D => BuildWithinPlan(descriptor, predicate),
                _ => null
            };

            if (plan == null || plan.IndexRanges == null || plan.IndexRanges.Count == 0)
            {
                return false;
            }

            var indexFieldExpression = BaseLiteDB.BsonExpression.Create(options.IndexFieldName).Source;
            if (string.IsNullOrWhiteSpace(indexFieldExpression))
            {
                return false;
            }

            if (!TryResolveIndexName(context, indexFieldExpression, out var persistedIndexName))
            {
                persistedIndexName = SanitizeIndexName(indexFieldExpression);
            }

            if (string.IsNullOrWhiteSpace(persistedIndexName))
            {
                return false;
            }

            indexExpression = indexFieldExpression;
            index = new SpatialMultiRangeIndex(persistedIndexName, plan.IndexRanges);

            var filters = new List<BaseLiteDB.BsonExpression>();
            if (plan.CoveringBounds.HasValue)
            {
                var bounding = this.BuildBoundingExpression(descriptor, plan.CoveringBounds.Value);
                if (bounding != null)
                {
                    filters.Add(bounding);
                }
            }

            if (filters.Count > 0)
            {
                additionalFilters = filters;
            }

            return true;
        }

        private static bool TryResolveIndexName(LiteDbPlugins.QueryPlanningContext context, string indexExpression, out string indexName)
        {
            indexName = string.Empty;

            var indexes = context?.Snapshot?.CollectionPage?.GetCollectionIndexes();
            if (indexes == null)
            {
                return false;
            }

            var matched = indexes.FirstOrDefault(candidate =>
                candidate != null
                && candidate.IndexType == 0
                && string.Equals(candidate.Expression, indexExpression, StringComparison.Ordinal));

            if (matched == null || string.IsNullOrWhiteSpace(matched.Name))
            {
                return false;
            }

            indexName = matched.Name;
            return true;
        }

        private static string SanitizeIndexName(string indexExpression)
        {
            if (string.IsNullOrWhiteSpace(indexExpression))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(indexExpression.Length);

            foreach (var ch in indexExpression)
            {
                if ((ch >= 'a' && ch <= 'z') ||
                    (ch >= 'A' && ch <= 'Z') ||
                    (ch >= '0' && ch <= '9'))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        private static ISpatialQueryPlan? BuildNear2DPlan(SpatialCollectionDescriptor descriptor, SpatialPredicate predicate)
        {
            if (!predicate.Center2D.HasValue || !predicate.Radius.HasValue)
            {
                return null;
            }

            var distanceMode = predicate.DistanceMode ?? descriptor.Settings.DistanceMode;

            return descriptor.EngineName switch
            {
                GeographicEngine.EngineName => SpatialGeographic.Near(descriptor, predicate.Center2D.Value, predicate.Radius.Value, distanceMode),
                Cartesian2DEngine.EngineName => SpatialCartesian2D.Near(descriptor, predicate.Center2D.Value, predicate.Radius.Value),
                _ => null
            };
        }

        private static ISpatialQueryPlan? BuildNear3DPlan(SpatialCollectionDescriptor descriptor, SpatialPredicate predicate)
        {
            if (!predicate.Center3D.HasValue || !predicate.Radius.HasValue)
            {
                return null;
            }

            return descriptor.EngineName switch
            {
                Cartesian3DEngine.EngineName => SpatialCartesian3D.Near(descriptor, predicate.Center3D.Value, predicate.Radius.Value),
                _ => null
            };
        }

        private static ISpatialQueryPlan? BuildWithinPlan(SpatialCollectionDescriptor descriptor, SpatialPredicate predicate)
        {
            if (!predicate.Bounds.HasValue)
            {
                return null;
            }

            return descriptor.EngineName switch
            {
                GeographicEngine.EngineName => SpatialGeographic.WithinBoundingBox(descriptor, predicate.Bounds.Value),
                Cartesian2DEngine.EngineName => SpatialCartesian2D.WithinBoundingBox(descriptor, predicate.Bounds.Value),
                Cartesian3DEngine.EngineName => SpatialCartesian3D.WithinBoundingBox(descriptor, predicate.Bounds.Value),
                _ => null
            };
        }

        private BaseLiteDB.BsonExpression? BuildBoundingExpression(SpatialCollectionDescriptor descriptor, BoundingBox bounds)
        {
            var registry = _services.Context?.Expressions;
            if (registry == null)
            {
                return null;
            }

            var boundingField = descriptor.Options?.BoundingBoxFieldName;
            if (string.IsNullOrWhiteSpace(boundingField))
            {
                return null;
            }

            var field = "$." + boundingField;
            var values = bounds.GetValues().ToArray();

            if (values.Length == 4)
            {
                var expression = $"({field} != null) AND {field}[0] <= @maxX AND {field}[2] >= @minX AND {field}[1] <= @maxY AND {field}[3] >= @minY";
                var parameters = new BaseLiteDB.BsonDocument
                {
                    ["maxX"] = values[2],
                    ["minX"] = values[0],
                    ["maxY"] = values[3],
                    ["minY"] = values[1]
                };

                return BaseLiteDB.BsonExpression.Create(expression, parameters, registry);
            }

            if (values.Length == 6)
            {
                var expression = $"({field} != null) AND {field}[0] <= @maxX AND {field}[3] >= @minX AND {field}[1] <= @maxY AND {field}[4] >= @minY AND {field}[2] <= @maxZ AND {field}[5] >= @minZ";
                var parameters = new BaseLiteDB.BsonDocument
                {
                    ["maxX"] = values[3],
                    ["minX"] = values[0],
                    ["maxY"] = values[4],
                    ["minY"] = values[1],
                    ["maxZ"] = values[5],
                    ["minZ"] = values[2]
                };

                return BaseLiteDB.BsonExpression.Create(expression, parameters, registry);
            }

            return null;
        }

        private enum SpatialPredicateType
        {
            Near2D,
            Near3D,
            InBox2D,
            InBox3D
        }

        private sealed class SpatialPredicate
        {
            private SpatialPredicate(
                SpatialPredicateType type,
                string geometryField,
                GeoPoint? center2D,
                GeoPoint3D? center3D,
                double? radius,
                BoundingBox? bounds,
                GeographicDistanceMode? distanceMode)
            {
                Type = type;
                GeometryField = geometryField;
                Center2D = center2D;
                Center3D = center3D;
                Radius = radius;
                Bounds = bounds;
                DistanceMode = distanceMode;
            }

            public SpatialPredicateType Type { get; }

            public string GeometryField { get; }

            public GeoPoint? Center2D { get; }

            public GeoPoint3D? Center3D { get; }

            public double? Radius { get; }

            public BoundingBox? Bounds { get; }

            public GeographicDistanceMode? DistanceMode { get; }

            public static bool TryParse(BaseLiteDB.BsonExpression expression, out SpatialPredicate? predicate)
            {
                predicate = null;

                if (expression == null || string.IsNullOrWhiteSpace(expression.Source))
                {
                    return false;
                }

                var parameters = expression.Parameters ?? new BaseLiteDB.BsonDocument();

                var source = TrimOuterParentheses(expression.Source.Trim());

                if (TryUnwrapBooleanEquals(source, parameters, out var unwrapped))
                {
                    source = TrimOuterParentheses(unwrapped);
                }

                var openParen = source.IndexOf('(');
                if (openParen <= 0 || !source.EndsWith(")", StringComparison.Ordinal))
                {
                    return false;
                }

                var function = source.Substring(0, openParen).Trim().ToUpperInvariant();
                var arguments = SplitArguments(source.Substring(openParen + 1, source.Length - openParen - 2));

                if (arguments.Count == 0)
                {
                    return false;
                }

                var geometryField = NormalizeField(arguments[0]);
                if (string.IsNullOrEmpty(geometryField))
                {
                    return false;
                }

                if (function == "SPATIAL_NEAR" && arguments.Count >= 3)
                {
                    var centerToken = ResolveArgument(arguments[1], parameters);
                    var radiusToken = ResolveArgument(arguments[2], parameters);
                    var formulaToken = arguments.Count >= 4 ? ResolveArgument(arguments[3], parameters) : BaseLiteDB.BsonValue.Null;

                    if (!SpatialBsonParser.TryGetDouble(radiusToken, out var radius))
                    {
                        return false;
                    }

                    if (SpatialBsonParser.TryGetGeoPoint(centerToken, out var point2D))
                    {
                        SpatialBsonParser.TryGetGeographicMode(formulaToken, out var mode);
                        predicate = new SpatialPredicate(SpatialPredicateType.Near2D, geometryField, point2D, null, radius, null, mode);
                        return true;
                    }

                    if (SpatialBsonParser.TryGetGeoPoint3D(centerToken, out var point3D))
                    {
                        predicate = new SpatialPredicate(SpatialPredicateType.Near3D, geometryField, null, point3D, radius, null, null);
                        return true;
                    }

                    return false;
                }

                if (function == "SPATIAL_IN_BOX" && arguments.Count >= 2)
                {
                    var boundsToken = ResolveArgument(arguments[1], parameters);
                    if (SpatialBsonParser.TryGetBoundingBox(boundsToken, out var bounds))
                    {
                        var type = bounds.Dimensions == 3 ? SpatialPredicateType.InBox3D : SpatialPredicateType.InBox2D;
                        predicate = new SpatialPredicate(type, geometryField, null, null, null, bounds, null);
                        return true;
                    }
                }

                return false;
            }

            private static IReadOnlyList<string> SplitArguments(string arguments)
            {
                var result = new List<string>();
                if (string.IsNullOrEmpty(arguments))
                {
                    return result;
                }

                var depth = 0;
                var span = arguments.AsSpan();
                var start = 0;

                for (var i = 0; i < span.Length; i++)
                {
                    var ch = span[i];

                    if (ch == '(')
                    {
                        depth++;
                        continue;
                    }

                    if (ch == ')')
                    {
                        depth = Math.Max(0, depth - 1);
                        continue;
                    }

                    if (ch == ',' && depth == 0)
                    {
                        result.Add(span.Slice(start, i - start).ToString().Trim());
                        start = i + 1;
                    }
                }

                if (start < span.Length)
                {
                    result.Add(span.Slice(start).ToString().Trim());
                }

                return result;
            }

            private static BaseLiteDB.BsonValue ResolveArgument(string token, BaseLiteDB.BsonDocument parameters)
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    return BaseLiteDB.BsonValue.Null;
                }

                token = token.Trim();

                if (token.StartsWith("@", StringComparison.Ordinal))
                {
                    var key = token.Substring(1);
                    if (parameters.TryGetValue(key, out var value))
                    {
                        return value;
                    }

                    if (parameters.TryGetValue("p" + key, out value))
                    {
                        return value;
                    }

                    if (key.StartsWith("p", StringComparison.OrdinalIgnoreCase) && parameters.TryGetValue(key.Substring(1), out value))
                    {
                        return value;
                    }

                    return BaseLiteDB.BsonValue.Null;
                }

                if (string.Equals(token, "NULL", StringComparison.OrdinalIgnoreCase))
                {
                    return BaseLiteDB.BsonValue.Null;
                }

                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    return new BaseLiteDB.BsonValue(number);
                }

                return BaseLiteDB.BsonValue.Null;
            }

            private static string? NormalizeField(string expression)
            {
                if (string.IsNullOrWhiteSpace(expression))
                {
                    return null;
                }

                var trimmed = expression.Trim();
                if (trimmed.StartsWith("$."))
                {
                    trimmed = trimmed.Substring(2);
                }

                if (trimmed.IndexOf("[*]", StringComparison.Ordinal) >= 0)
                {
                    trimmed = trimmed.Replace("[*]", string.Empty);
                }

                return trimmed;
            }

            private static string TrimOuterParentheses(string source)
            {
                if (string.IsNullOrWhiteSpace(source))
                {
                    return source ?? string.Empty;
                }

                source = source.Trim();

                while (source.Length >= 2 && source[0] == '(' && source[source.Length - 1] == ')')
                {
                    var depth = 0;
                    var wraps = true;

                    for (var i = 0; i < source.Length; i++)
                    {
                        var ch = source[i];

                        if (ch == '(')
                        {
                            depth++;
                        }
                        else if (ch == ')')
                        {
                            depth--;

                            if (depth == 0 && i < source.Length - 1)
                            {
                                wraps = false;
                                break;
                            }
                        }
                    }

                    if (!wraps || depth != 0)
                    {
                        break;
                    }

                    source = source.Substring(1, source.Length - 2).Trim();
                }

                return source;
            }

            private static bool TryUnwrapBooleanEquals(string source, BaseLiteDB.BsonDocument parameters, out string unwrapped)
            {
                unwrapped = source;

                if (string.IsNullOrWhiteSpace(source))
                {
                    return false;
                }

                var index = source.LastIndexOf('=');
                if (index <= 0)
                {
                    return false;
                }

                var previous = source[index - 1];
                if (previous == '!' || previous == '<' || previous == '>')
                {
                    return false;
                }

                var left = source.Substring(0, index).Trim();
                var rightToken = source.Substring(index + 1).Trim();

                if (rightToken.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    unwrapped = left;
                    return true;
                }

                if (rightToken.StartsWith("@", StringComparison.Ordinal))
                {
                    var resolved = ResolveArgument(rightToken, parameters);

                    if (resolved.IsBoolean && resolved.AsBoolean)
                    {
                        unwrapped = left;
                        return true;
                    }
                }

                return false;
            }
        }
    }
}

