using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Plugins;

namespace LiteDB.Plugins.Storage
{
    /// <summary>
    /// Thread-safe <see cref="IPageFactoryRegistry"/> implementation used by <see cref="DefaultPluginContext"/> so each database maintains isolated factory registrations.
    /// </summary>
    public sealed class PluginPageFactoryRegistry : IPageFactoryRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, PageFactoryRegistration> _factories = new Dictionary<string, PageFactoryRegistration>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<byte, PageFactoryRegistration> _factoriesByCode = new Dictionary<byte, PageFactoryRegistration>();

        /// <summary>
        /// Reserved page type code ranges for known plugins.
        /// Key: Plugin ID, Value: (MinCode, MaxCode)
        /// </summary>
        private static readonly Dictionary<string, (byte Min, byte Max)> ReservedRanges = new Dictionary<string, (byte, byte)>(StringComparer.Ordinal)
        {
            { "LiteDB.Vector", (0xE0, 0xEF) } // 224-239
        };

        /// <inheritdoc />
        public void Register(PageFactoryRegistration registration)
        {
            if (registration == null) throw new ArgumentNullException(nameof(registration));

            lock (_sync)
            {
                // Validate numeric code is within reserved range if plugin ID is known
                if (!string.IsNullOrWhiteSpace(registration.PluginId) &&
                    ReservedRanges.TryGetValue(registration.PluginId, out var range))
                {
                    if (registration.NumericCode < range.Min || registration.NumericCode > range.Max)
                    {
                        throw new InvalidOperationException(
                            $"Plugin '{registration.PluginId}' attempted to register page type code 0x{registration.NumericCode:X2} " +
                            $"outside its reserved range 0x{range.Min:X2}-0x{range.Max:X2}.");
                    }
                }

                // Check for conflicts with existing registrations by numeric code
                if (_factoriesByCode.TryGetValue(registration.NumericCode, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Page type code 0x{registration.NumericCode:X2} is already registered by plugin '{existing.PluginId}' " +
                        $"for page type '{existing.PageType}'. Plugin '{registration.PluginId}' cannot use the same code for '{registration.PageType}'.");
                }

                _factories[registration.PageType] = registration;
                _factoriesByCode[registration.NumericCode] = registration;
            }
        }

        /// <inheritdoc />
        public bool TryGet(string pageType, out PageFactoryRegistration registration)
        {
            if (string.IsNullOrWhiteSpace(pageType))
            {
                registration = null;
                return false;
            }

            lock (_sync)
            {
                return _factories.TryGetValue(pageType, out registration);
            }
        }

        /// <inheritdoc />
        public IReadOnlyCollection<PageFactoryRegistration> Registered
        {
            get
            {
                lock (_sync)
                {
                    return _factories.Values.ToArray();
                }
            }
        }
    }
}
