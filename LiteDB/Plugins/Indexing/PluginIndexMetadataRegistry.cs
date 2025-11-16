using System;
using System.Collections.Generic;

namespace LiteDB.Plugins.Indexing
{
    /// <summary>
    /// Thread-safe implementation of the plugin index metadata registry.
    /// </summary>
    internal sealed class PluginIndexMetadataRegistry : IPluginIndexMetadataRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, PluginIndexMetadataDescriptor> _descriptors =
            new Dictionary<string, PluginIndexMetadataDescriptor>(StringComparer.Ordinal);

        public void Register(PluginIndexMetadataDescriptor descriptor)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));

            lock (_sync)
            {
                if (_descriptors.ContainsKey(descriptor.IndexKind))
                {
                    throw new InvalidOperationException(
                        $"An index metadata descriptor for kind '{descriptor.IndexKind}' is already registered.");
                }

                _descriptors[descriptor.IndexKind] = descriptor;
            }
        }

        public bool TryGet(string indexKind, out PluginIndexMetadataDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(indexKind))
            {
                descriptor = null;
                return false;
            }

            lock (_sync)
            {
                return _descriptors.TryGetValue(indexKind, out descriptor);
            }
        }

        public PluginIndexMetadataDescriptor Get(string indexKind)
        {
            if (!TryGet(indexKind, out var descriptor))
            {
                throw new KeyNotFoundException(
                    $"No index metadata descriptor registered for kind '{indexKind}'.");
            }

            return descriptor;
        }
    }
}
