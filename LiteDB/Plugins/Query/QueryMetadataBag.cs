using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Stores plugin-managed query metadata with version tracking and strongly typed accessors.
    /// </summary>
    public sealed class QueryMetadataBag
    {
        private readonly Dictionary<string, object> _state;
        private readonly StringComparer _comparer = StringComparer.Ordinal;
        private HashSet<string> _reservedLookup;
        private string[] _reservedKeys;
        private ReadOnlyDictionary<string, object> _readonlyView;

        /// <summary>
        /// Initializes a new instance of the <see cref="QueryMetadataBag"/> class from a descriptor.
        /// </summary>
        /// <param name="descriptor">Registered metadata descriptor.</param>
        public QueryMetadataBag(QueryMetadataDescriptor descriptor)
            : this(
                descriptor?.PluginId,
                descriptor?.Version ?? throw new ArgumentNullException(nameof(descriptor)),
                descriptor.ReservedKeys,
                null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="QueryMetadataBag"/> class.
        /// </summary>
        /// <param name="pluginId">Identifier of the plugin that owns the metadata.</param>
        /// <param name="version">Metadata schema version.</param>
        /// <param name="reservedKeys">Collection of reserved keys accepted by the bag.</param>
        public QueryMetadataBag(string pluginId, int version, IEnumerable<string> reservedKeys)
            : this(pluginId, version, reservedKeys, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="QueryMetadataBag"/> class with initial values.
        /// </summary>
        /// <param name="pluginId">Identifier of the plugin that owns the metadata.</param>
        /// <param name="version">Metadata schema version.</param>
        /// <param name="reservedKeys">Collection of reserved keys accepted by the bag.</param>
        /// <param name="initialValues">Optional initial state to seed the bag.</param>
        public QueryMetadataBag(string pluginId, int version, IEnumerable<string> reservedKeys, IEnumerable<KeyValuePair<string, object>> initialValues)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException("Plugin identifier must be provided.", nameof(pluginId));
            }

            if (version < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(version), "Metadata version must be a positive integer.");
            }

            PluginId = pluginId;
            Version = version;

            ResetReservedKeys(reservedKeys);

            _state = new Dictionary<string, object>(_comparer);
            _readonlyView = new ReadOnlyDictionary<string, object>(_state);

            if (initialValues != null)
            {
                foreach (var pair in initialValues)
                {
                    Set(pair.Key, pair.Value);
                }
            }
        }

        /// <summary>
        /// Gets the plugin identifier associated with stored metadata.
        /// </summary>
        public string PluginId { get; }

        /// <summary>
        /// Gets the schema version for the metadata bag.
        /// </summary>
        public int Version { get; private set; }

        /// <summary>
        /// Gets the reserved keys declared by the plugin.
        /// </summary>
        public IReadOnlyCollection<string> ReservedKeys => _reservedKeys;

        /// <summary>
        /// Gets a read-only view of the currently stored metadata entries.
        /// </summary>
        public IReadOnlyDictionary<string, object> Values => _readonlyView;

        /// <summary>
        /// Gets the number of value entries stored in the bag.
        /// </summary>
        public int Count => _state.Count;

        /// <summary>
        /// Determines whether any metadata has been recorded.
        /// </summary>
        public bool IsEmpty => _state.Count == 0;

        /// <summary>
        /// Records or replaces a metadata value for the specified key.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="key">Reserved metadata key.</param>
        /// <param name="value">Value to store.</param>
        public void Set<T>(string key, T value)
        {
            ValidateKey(key);
            EnsureKeyReserved(key);

            _state[key] = value;
        }

        /// <summary>
        /// Attempts to retrieve a metadata value by key.
        /// </summary>
        /// <typeparam name="T">The expected value type.</typeparam>
        /// <param name="key">Reserved metadata key.</param>
        /// <param name="value">Output variable receiving the value.</param>
        /// <returns>True when the key exists with a compatible type.</returns>
        public bool TryGet<T>(string key, out T value)
        {
            ValidateKey(key);

            if (!_state.TryGetValue(key, out var raw))
            {
                value = default;
                return false;
            }

            if (raw == null)
            {
                if (default(T) != null)
                {
                    throw new InvalidCastException($"Metadata key '{key}' for plugin '{PluginId}' contains a null value which cannot be assigned to type '{typeof(T).FullName}'.");
                }

                value = default;
                return true;
            }

            if (raw is T matched)
            {
                value = matched;
                return true;
            }

            throw new InvalidCastException($"Metadata key '{key}' for plugin '{PluginId}' contains a value of type '{raw.GetType().FullName}' which cannot be cast to '{typeof(T).FullName}'.");
        }

        /// <summary>
        /// Retrieves the metadata value associated with the supplied key.
        /// </summary>
        /// <typeparam name="T">The expected value type.</typeparam>
        /// <param name="key">Reserved metadata key.</param>
        /// <returns>The stored value.</returns>
        public T Get<T>(string key)
        {
            if (!TryGet<T>(key, out var value))
            {
                throw new KeyNotFoundException($"Metadata key '{key}' not found for plugin '{PluginId}'.");
            }

            return value;
        }

        /// <summary>
        /// Returns a snapshot of the underlying metadata dictionary.
        /// </summary>
        /// <returns>A shallow copy of stored metadata.</returns>
        public IReadOnlyDictionary<string, object> Snapshot()
        {
            return new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(_state, _comparer));
        }

        /// <summary>
        /// Removes the metadata entry associated with the supplied key.
        /// </summary>
        /// <param name="key">Reserved metadata key.</param>
        /// <returns>True when the key existed and was removed.</returns>
        public bool Remove(string key)
        {
            ValidateKey(key);
            return _state.Remove(key);
        }

        /// <summary>
        /// Clears all metadata entries.
        /// </summary>
        public void Clear()
        {
            _state.Clear();
        }

        /// <summary>
        /// Updates the schema version of the metadata bag.
        /// </summary>
        /// <param name="version">The new schema version.</param>
        public void SetVersion(int version)
        {
            if (version < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(version), "Metadata version must be a positive integer.");
            }

            if (version < Version)
            {
                throw new InvalidOperationException($"Cannot downgrade metadata version from {Version} to {version} for plugin '{PluginId}'.");
            }

            Version = version;
        }

        /// <summary>
        /// Updates the reserved key list using the supplied descriptor while validating existing state.
        /// </summary>
        /// <param name="descriptor">Updated metadata descriptor.</param>
        public void ApplyDescriptor(QueryMetadataDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            if (!string.Equals(descriptor.PluginId, PluginId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Descriptor plugin id '{descriptor.PluginId}' does not match bag plugin id '{PluginId}'.");
            }

            if (descriptor.Version < Version)
            {
                throw new InvalidOperationException($"Cannot downgrade metadata version from {Version} to {descriptor.Version} for plugin '{PluginId}'.");
            }

            EnsureExistingEntriesSupported(descriptor.ReservedKeys);
            ResetReservedKeys(descriptor.ReservedKeys);

            Version = descriptor.Version;
        }

        /// <summary>
        /// Determines whether the bag matches the supplied descriptor.
        /// </summary>
        /// <param name="descriptor">Descriptor to compare.</param>
        /// <returns>True when plugin identifier and version match.</returns>
        public bool IsCompatibleWith(QueryMetadataDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return false;
            }

            return string.Equals(descriptor.PluginId, PluginId, StringComparison.Ordinal) && descriptor.Version == Version;
        }

        /// <summary>
        /// Creates a shallow copy of the current metadata bag.
        /// </summary>
        /// <returns>Cloned metadata bag instance.</returns>
        public QueryMetadataBag Clone()
        {
            return new QueryMetadataBag(PluginId, Version, _reservedKeys, _state);
        }

        private QueryMetadataBag(string pluginId, int version, IEnumerable<string> reservedKeys, IDictionary<string, object> state)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException("Plugin identifier must be provided.", nameof(pluginId));
            }

            if (version < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(version), "Metadata version must be a positive integer.");
            }

            PluginId = pluginId;
            Version = version;

            ResetReservedKeys(reservedKeys);

            _state = new Dictionary<string, object>(state ?? new Dictionary<string, object>(_comparer), _comparer);
            _readonlyView = new ReadOnlyDictionary<string, object>(_state);
        }

        private void ResetReservedKeys(IEnumerable<string> reservedKeys)
        {
            _reservedKeys = reservedKeys != null
                ? ValidateReservedKeys(reservedKeys)
                : Array.Empty<string>();

            _reservedLookup = new HashSet<string>(_reservedKeys, _comparer);
        }

        private string[] ValidateReservedKeys(IEnumerable<string> reservedKeys)
        {
            var collected = new List<string>();
            var seen = new HashSet<string>(_comparer);

            foreach (var key in reservedKeys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new ArgumentException("Reserved metadata keys must be non-empty strings.", nameof(reservedKeys));
                }

                if (!seen.Add(key))
                {
                    throw new ArgumentException($"Reserved metadata key '{key}' was provided more than once.", nameof(reservedKeys));
                }

                collected.Add(key);
            }

            return collected.ToArray();
        }

        private void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Metadata key must be provided.", nameof(key));
            }
        }

        private void EnsureKeyReserved(string key)
        {
            if (_reservedLookup.Count > 0 && !_reservedLookup.Contains(key))
            {
                throw new InvalidOperationException($"Metadata key '{key}' has not been reserved by plugin '{PluginId}'.");
            }
        }

        private void EnsureExistingEntriesSupported(IEnumerable<string> reservedKeys)
        {
            if (reservedKeys == null)
            {
                return;
            }

            var lookup = new HashSet<string>(reservedKeys, _comparer);

            if (lookup.Count == 0 && _state.Count == 0)
            {
                return;
            }

            if (lookup.Count == 0 && _state.Count > 0)
            {
                throw new InvalidOperationException($"The descriptor for plugin '{PluginId}' does not declare reserved keys, but the metadata bag already contains values.");
            }

            foreach (var key in _state.Keys)
            {
                if (!lookup.Contains(key))
                {
                    throw new InvalidOperationException($"Existing metadata key '{key}' is not present in the descriptor for plugin '{PluginId}'.");
                }
            }
        }
    }
}
