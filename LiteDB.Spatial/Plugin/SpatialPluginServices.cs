extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial;
using LiteDB.Spatial.Plugin.Linq;
using LiteDB.Spatial.Plugin.QueryPlanning;
using LiteDB.Spatial.Plugin.Runtime;
using BaseLiteDB = LiteDbBase::LiteDB;
using LiteDbPlugins = LiteDbBase::LiteDB.Plugins;

namespace LiteDB.Spatial.Plugin
{
    internal sealed class SpatialPluginServices
    {
        private readonly ConcurrentDictionary<string, SpatialCollectionDescriptor> _descriptorsByCollection = new ConcurrentDictionary<string, SpatialCollectionDescriptor>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _geometryFieldsByIndex = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<Type, SpatialLinqResolver> _resolverCache = new ConcurrentDictionary<Type, SpatialLinqResolver>();
        private readonly SpatialMetadataStore _metadataStore;

        public SpatialPluginServices(BaseLiteDB.LiteDatabase database, LiteDbPlugins.ILitePluginContext context)
        {
            Database = database ?? throw new ArgumentNullException(nameof(database));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            _metadataStore = new SpatialMetadataStore((BaseLiteDB.ILiteDatabase)database);
        }

        public BaseLiteDB.LiteDatabase Database { get; }

        public LiteDbPlugins.ILitePluginContext Context { get; }

        public SpatialLinqResolver GetOrCreateResolver(Type declaringType)
        {
            if (declaringType == null) throw new ArgumentNullException(nameof(declaringType));

            return _resolverCache.GetOrAdd(declaringType, _ => new SpatialLinqResolver(this));
        }

        public SpatialQueryPlanningRule CreatePlanningRule()
        {
            return new SpatialQueryPlanningRule(this);
        }

        public bool TryHandleEnsureIndex(LiteDbPlugins.EnsureIndexContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (context.Unique)
            {
                Log(LiteDbPlugins.LogLevel.Warning, $"Spatial indexes do not support unique constraints. Collection='{context.CollectionName}'.");
                return false;
            }

            var expression = context.Expression?.Source;
            if (string.IsNullOrWhiteSpace(expression))
            {
                return false;
            }

            var fieldPath = NormalizeFieldExpression(expression);
            if (string.IsNullOrEmpty(fieldPath))
            {
                return false;
            }

            if (!TryResolveDescriptor(context.CollectionName, fieldPath, out var descriptor) &&
                !TryCreateDescriptor(context, fieldPath, out descriptor))
            {
                return false;
            }

            if (descriptor == null)
            {
                return false;
            }

            CacheGeometryField(context.Name, descriptor);
            context.SetResult(true);
            return true;
        }

        public bool TryResolveDescriptor(string collection, string geometryField, out SpatialCollectionDescriptor? descriptor)
        {
            descriptor = null;

            if (string.IsNullOrWhiteSpace(collection) || string.IsNullOrWhiteSpace(geometryField))
            {
                return false;
            }

            if (_descriptorsByCollection.TryGetValue(collection, out var cached))
            {
                descriptor = cached;
                return cached.GeometryFieldName.Equals(geometryField, StringComparison.OrdinalIgnoreCase);
            }

            if (_metadataStore.TryGetDescriptor(collection, out var persisted))
            {
                descriptor = persisted;
                _descriptorsByCollection[collection] = persisted!;
                return persisted!.GeometryFieldName.Equals(geometryField, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public IEnumerable<SpatialCollectionDescriptor> GetDescriptors()
        {
            if (_descriptorsByCollection.Count == 0)
            {
                var metadataCollection = Database.GetCollection(SpatialMetadataStore.MetadataCollectionName);
                foreach (var document in metadataCollection.FindAll())
                {
                    if (!document.TryGetValue("collection", out var collectionValue) || !collectionValue.IsString)
                    {
                        continue;
                    }

                    var collectionName = collectionValue.AsString;
                    if (_descriptorsByCollection.ContainsKey(collectionName))
                    {
                        continue;
                    }

                    if (_metadataStore.TryGetDescriptor(collectionName, out var descriptor))
                    {
                        _descriptorsByCollection[collectionName] = descriptor;
                    }
                }
            }

            return _descriptorsByCollection.Values;
        }

        public void RegisterDescriptor(SpatialCollectionDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            _descriptorsByCollection[descriptor.CollectionName] = descriptor;
            _metadataStore.SaveDescriptor(descriptor.CollectionName, descriptor);
        }

        public bool TryGetGeometryFieldByIndex(string indexName, out string? field)
        {
            return _geometryFieldsByIndex.TryGetValue(indexName ?? string.Empty, out field);
        }

        internal bool TryGetDescriptor(string collectionName, out SpatialCollectionDescriptor? descriptor)
        {
            descriptor = null;

            if (string.IsNullOrWhiteSpace(collectionName))
            {
                return false;
            }

            if (_descriptorsByCollection.TryGetValue(collectionName, out var cached))
            {
                descriptor = cached;
                return true;
            }

            if (_metadataStore.TryGetDescriptor(collectionName, out var persisted))
            {
                descriptor = persisted;
                _descriptorsByCollection[collectionName] = persisted!;
                return true;
            }

            return false;
        }

        internal SpatialMetadataStore MetadataStore => _metadataStore;

        private bool TryCreateDescriptor(LiteDbPlugins.EnsureIndexContext context, string geometryField, out SpatialCollectionDescriptor? descriptor)
        {
            descriptor = null;

            var entityType = context.EntityType;
            if (entityType == null || entityType == typeof(BaseLiteDB.BsonDocument))
            {
                Log(LiteDbPlugins.LogLevel.Debug, $"Spatial plugin skipped EnsureIndex interception for '{context.CollectionName}.{geometryField}' because the entity type is not strongly typed.");
                return false;
            }

            var mapper = context.Mapper;
            if (mapper == null)
            {
                Log(LiteDbPlugins.LogLevel.Debug, $"Spatial plugin skipped EnsureIndex interception for '{context.CollectionName}.{geometryField}' because the mapper is not available.");
                return false;
            }

            try
            {
                var entityMapper = mapper.GetEntityMapper(entityType);
                entityMapper?.WaitForInitialization();

                var member = entityMapper?.Members.FirstOrDefault(m =>
                    string.Equals(m.FieldName, geometryField, StringComparison.OrdinalIgnoreCase));

                if (member == null || member.IsIgnore)
                {
                    Log(LiteDbPlugins.LogLevel.Debug, $"Spatial plugin could not locate a mapped member for '{context.CollectionName}.{geometryField}'.");
                    return false;
                }

                var spatialType = ResolveSpatialMemberType(member);
                if (spatialType == null)
                {
                    Log(LiteDbPlugins.LogLevel.Debug, $"Spatial plugin encountered unsupported member type '{member.DataType.FullName}' for '{context.CollectionName}.{geometryField}'.");
                    return false;
                }

                var options = SpatialMemberOptionsRegistry.Resolve(entityType, member.MemberName);
                var descriptorCandidate = CreateDescriptor(context, geometryField, spatialType, options);
                if (descriptorCandidate == null)
                {
                    return false;
                }

                CacheDescriptor(descriptorCandidate);

                var reloaded = TryReloadDescriptor(context.CollectionName, descriptorCandidate);
                descriptor = reloaded ?? descriptorCandidate;

                if (descriptor == null)
                {
                    return false;
                }

                if (!ReferenceEquals(descriptor, descriptorCandidate))
                {
                    CacheDescriptor(descriptor);
                }

                Log(LiteDbPlugins.LogLevel.Information, $"Configured spatial metadata for '{context.CollectionName}.{geometryField}' using engine '{descriptor.EngineName}'.");
                return true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException && ex is not AccessViolationException)
            {
                Log(LiteDbPlugins.LogLevel.Error, $"Spatial plugin failed to build metadata for '{context.CollectionName}.{geometryField}': {ex.Message}");
                return false;
            }
        }

        private SpatialCollectionDescriptor? CreateDescriptor(
            LiteDbPlugins.EnsureIndexContext context,
            string geometryField,
            Type spatialType,
            SpatialMemberOptions? options)
        {
            var engine = ResolveEngine(spatialType, options);
            if (engine == SpatialEngineKind.Automatic)
            {
                Log(LiteDbPlugins.LogLevel.Warning, $"Spatial plugin could not determine a spatial engine for '{context.CollectionName}.{geometryField}'. Apply a SpatialOptionsAttribute or fluent configuration to declare engine details.");
                return null;
            }

            if (!ValidateEngineCompatibility(spatialType, engine, context.CollectionName, geometryField))
            {
                return null;
            }

            var effectiveOptions = options != null ? options.ApplyTo(new SpatialIndexOptions()) : new SpatialIndexOptions();

            switch (engine)
            {
                case SpatialEngineKind.Geographic2D:
                    {
                        var distanceMode = options?.DistanceMode ?? GeographicDistanceMode.Haversine;
                        return SpatialInitializer.EnsureGeographic(
                            Database,
                            _metadataStore,
                            context.CollectionName,
                            geometryField,
                            effectiveOptions,
                            distanceMode);
                    }

                case SpatialEngineKind.Cartesian2D:
                    {
                        if (!TryResolveDomain(spatialType, options, context.CollectionName, geometryField, 2, out var domain))
                        {
                            return null;
                        }

                        return SpatialInitializer.EnsureCartesian2D(
                            Database,
                            _metadataStore,
                            context.CollectionName,
                            geometryField,
                            domain,
                            effectiveOptions);
                    }

                case SpatialEngineKind.Cartesian3D:
                    {
                        if (!TryResolveDomain(spatialType, options, context.CollectionName, geometryField, 3, out var domain))
                        {
                            return null;
                        }

                        return SpatialInitializer.EnsureCartesian3D(
                            Database,
                            _metadataStore,
                            context.CollectionName,
                            geometryField,
                            domain,
                            effectiveOptions);
                    }

                default:
                    Log(LiteDbPlugins.LogLevel.Warning, $"Spatial plugin does not support engine '{engine}' when configuring '{context.CollectionName}.{geometryField}'.");
                    return null;
            }
        }

        private static SpatialEngineKind ResolveEngine(Type spatialType, SpatialMemberOptions? options)
        {
            if (options?.Engine.HasValue == true && options.Engine.Value != SpatialEngineKind.Automatic)
            {
                return options.Engine.Value;
            }

            if (spatialType == typeof(GeoPoint))
            {
                return SpatialEngineKind.Geographic2D;
            }

            if (spatialType == typeof(GeoPoint3D))
            {
                return SpatialEngineKind.Cartesian3D;
            }

            if (spatialType == typeof(BoundingBox))
            {
                var configuredDomain = options?.Domain;
                if (configuredDomain.HasValue)
                {
                    return configuredDomain.Value.Dimensions == 3
                        ? SpatialEngineKind.Cartesian3D
                        : SpatialEngineKind.Cartesian2D;
                }

                return SpatialEngineKind.Automatic;
            }

            return SpatialEngineKind.Automatic;
        }

        private bool ValidateEngineCompatibility(Type spatialType, SpatialEngineKind engine, string collectionName, string geometryField)
        {
            if (spatialType == typeof(GeoPoint3D) && engine != SpatialEngineKind.Cartesian3D)
            {
                Log(LiteDbPlugins.LogLevel.Warning, $"Spatial plugin cannot provision '{collectionName}.{geometryField}' because engine '{engine}' is incompatible with GeoPoint3D members.");
                return false;
            }

            if (spatialType == typeof(GeoPoint) && engine == SpatialEngineKind.Cartesian3D)
            {
                Log(LiteDbPlugins.LogLevel.Warning, $"Spatial plugin cannot provision '{collectionName}.{geometryField}' using the Cartesian3D engine. Choose Geographic2D or Cartesian2D.");
                return false;
            }

            if (spatialType == typeof(BoundingBox) && engine == SpatialEngineKind.Geographic2D)
            {
                Log(LiteDbPlugins.LogLevel.Warning, $"Spatial plugin cannot provision '{collectionName}.{geometryField}' as a geographic index because BoundingBox members require a Cartesian domain.");
                return false;
            }

            return true;
        }

        private bool TryResolveDomain(
            Type spatialType,
            SpatialMemberOptions? options,
            string collectionName,
            string geometryField,
            int requiredDimensions,
            out BoundingBox domain)
        {
            domain = default;

            if (options?.Domain is BoundingBox configured)
            {
                if (configured.Dimensions != requiredDimensions)
                {
                    Log(LiteDbPlugins.LogLevel.Warning, $"Spatial plugin expected a {requiredDimensions}D domain for '{collectionName}.{geometryField}' but received metadata describing {configured.Dimensions} dimensions.");
                    return false;
                }

                domain = configured;
                return true;
            }

            if (requiredDimensions == 3 && spatialType == typeof(GeoPoint3D))
            {
                domain = BoundingBox.From3D(-180, -90, -180, 180, 90, 180);
                Log(LiteDbPlugins.LogLevel.Debug, $"Spatial plugin defaulted the Cartesian3D domain for '{collectionName}.{geometryField}' to global bounds.");
                return true;
            }

            Log(LiteDbPlugins.LogLevel.Warning, $"Spatial plugin requires a {requiredDimensions}D domain definition to configure '{collectionName}.{geometryField}'.");
            return false;
        }

        private SpatialCollectionDescriptor? TryReloadDescriptor(string collectionName, SpatialCollectionDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                return null;
            }

            if (_metadataStore.TryGetDescriptor(collectionName, out var persisted))
            {
                return persisted;
            }

            Log(LiteDbPlugins.LogLevel.Debug, $"Spatial plugin persisted metadata for '{collectionName}' but could not reload the descriptor immediately. Using the freshly created descriptor.");
            return null;
        }

        private void CacheDescriptor(SpatialCollectionDescriptor? descriptor)
        {
            if (descriptor == null)
            {
                return;
            }

            _descriptorsByCollection[descriptor.CollectionName] = descriptor;
        }

        private void CacheGeometryField(string indexName, SpatialCollectionDescriptor? descriptor)
        {
            if (descriptor == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(indexName))
            {
                _geometryFieldsByIndex[indexName] = descriptor.GeometryFieldName;
            }

            var spatialIndexName = descriptor.Options?.IndexFieldName;
            if (!string.IsNullOrWhiteSpace(spatialIndexName))
            {
                _geometryFieldsByIndex[spatialIndexName] = descriptor.GeometryFieldName;
            }

            var boundingFieldName = descriptor.Options?.BoundingBoxFieldName;
            if (!string.IsNullOrWhiteSpace(boundingFieldName))
            {
                _geometryFieldsByIndex[boundingFieldName] = descriptor.GeometryFieldName;
            }
        }

        private static string? NormalizeFieldExpression(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return null;
            }

            var trimmed = expression.Trim();
            if (!trimmed.StartsWith("$.") || trimmed.Length <= 2)
            {
                return null;
            }

            var field = trimmed.Substring(2);
            if (field.IndexOf("[*]", StringComparison.Ordinal) >= 0)
            {
                field = field.Replace("[*]", string.Empty);
            }

            return field;
        }

        private void Log(LiteDbPlugins.LogLevel level, string message)
        {
            Context?.Logger?.Write(level, $"[SpatialPlugin] {message}");
        }

        private static Type? ResolveSpatialMemberType(BaseLiteDB.MemberMapper member)
        {
            if (member == null)
            {
                return null;
            }

            var candidate = member.DataType;

            if (member.IsEnumerable && member.UnderlyingType != null && member.UnderlyingType != typeof(object))
            {
                candidate = member.UnderlyingType;
            }

            candidate = Nullable.GetUnderlyingType(candidate) ?? candidate;

            if (candidate == typeof(GeoPoint) || candidate == typeof(GeoPoint3D) || candidate == typeof(BoundingBox))
            {
                return candidate;
            }

            return null;
        }

    }
}
