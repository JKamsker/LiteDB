using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LiteDB.Plugins.Storage
{
    /// <summary>
    /// Delegate invoked to construct a plugin-managed page.
    /// </summary>
    /// <param name="context">Opaque construction context provided by the engine.</param>
    /// <returns>The constructed page instance.</returns>
    public delegate object PageFactoryDelegate(object context);

    /// <summary>
    /// Delegate invoked to serialize plugin-managed page metadata.
    /// </summary>
    /// <param name="context">Opaque serialization context.</param>
    public delegate Task PageMetadataSerializer(object context);

    /// <summary>
    /// Delegate invoked to participate in rebuild flows for plugin-managed pages.
    /// </summary>
    /// <param name="context">Opaque rebuild context.</param>
    public delegate Task PageRebuildHook(object context);

    /// <summary>
    /// Registry contract that allows plugins to register page factories and related callbacks.
    /// </summary>
    public interface IPageFactoryRegistry
    {
        /// <summary>
        /// Registers the supplied page factory descriptor.
        /// </summary>
        /// <param name="registration">Descriptor describing the plugin-managed page.</param>
        void Register(PageFactoryRegistration registration);

        /// <summary>
        /// Attempts to resolve a descriptor by its logical page type.
        /// </summary>
        /// <param name="pageType">Logical page classification.</param>
        /// <param name="registration">Resolved descriptor if available.</param>
        /// <returns>True when a descriptor exists.</returns>
        bool TryGet(string pageType, out PageFactoryRegistration registration);

        /// <summary>
        /// Gets the registered page factory descriptors.
        /// </summary>
        IReadOnlyCollection<PageFactoryRegistration> Registered { get; }
    }

    /// <summary>
    /// Describes a plugin-provided page factory and associated lifecycle hooks.
    /// </summary>
    public sealed class PageFactoryRegistration
    {
        public PageFactoryRegistration(
            string pluginId,
            string pageType,
            string compatibilityRange,
            PageFactoryDelegate factory,
            PageMetadataSerializer metadataSerializer = null,
            PageRebuildHook rebuildHook = null)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException("Plugin identifier must be provided.", nameof(pluginId));
            }

            if (string.IsNullOrWhiteSpace(pageType))
            {
                throw new ArgumentException("Page type must be provided.", nameof(pageType));
            }

            PluginId = pluginId;
            PageType = pageType;
            CompatibilityRange = compatibilityRange ?? throw new ArgumentNullException(nameof(compatibilityRange));
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            MetadataSerializer = metadataSerializer;
            RebuildHook = rebuildHook;
        }

        /// <summary>
        /// Gets the identifier for the plugin that owns the page factory.
        /// </summary>
        public string PluginId { get; }

        /// <summary>
        /// Gets the logical page type the factory handles.
        /// </summary>
        public string PageType { get; }

        /// <summary>
        /// Gets the declared compatibility range for the page format.
        /// </summary>
        public string CompatibilityRange { get; }

        /// <summary>
        /// Gets the delegate responsible for constructing the page.
        /// </summary>
        public PageFactoryDelegate Factory { get; }

        /// <summary>
        /// Gets the optional metadata serializer delegate.
        /// </summary>
        public PageMetadataSerializer MetadataSerializer { get; }

        /// <summary>
        /// Gets the optional rebuild hook delegate.
        /// </summary>
        public PageRebuildHook RebuildHook { get; }
    }
}
