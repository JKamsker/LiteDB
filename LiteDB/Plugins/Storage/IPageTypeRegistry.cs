using System;
using System.Collections.Generic;
namespace LiteDB.Plugins.Storage
{
    /// <summary>
    /// Registry for plugin-defined page types backed by deterministic numeric codes.
    /// Plugins register their factory once per database during <see cref="ILitePlugin.Initialize"/>.
    /// </summary>
    public interface IPageTypeRegistry
    {
        /// <summary>
        /// Registers the supplied page factory descriptor.
        /// </summary>
        /// <param name="registration">Descriptor describing the plugin-managed page.</param>
        void Register(PageFactoryRegistration registration);

        /// <summary>
        /// Attempts to resolve a descriptor by its numeric page code.
        /// </summary>
        /// <param name="pageTypeCode">Numeric page type identifier stored on disk.</param>
        /// <param name="registration">Resolved descriptor when found.</param>
        /// <returns>True when a matching descriptor exists.</returns>
        bool TryGet(byte pageTypeCode, out PageFactoryRegistration registration);

        /// <summary>
        /// Attempts to resolve a descriptor by its logical name irrespective of plugin ownership.
        /// </summary>
        /// <param name="pageTypeName">Logical page type name.</param>
        /// <param name="registration">Resolved descriptor when found.</param>
        /// <returns>True when a descriptor exists.</returns>
        bool TryGet(string pageTypeName, out PageFactoryRegistration registration);

        /// <summary>
        /// Attempts to resolve a descriptor by plugin identifier and logical name.
        /// </summary>
        /// <param name="pluginId">Identifier for the plugin that owns the page.</param>
        /// <param name="pageTypeName">Logical page type name.</param>
        /// <param name="registration">Resolved descriptor when found.</param>
        /// <returns>True when a descriptor exists.</returns>
        bool TryGetByName(string pluginId, string pageTypeName, out PageFactoryRegistration registration);

        /// <summary>
        /// Gets the currently registered descriptors.
        /// </summary>
        IReadOnlyCollection<PageFactoryRegistration> Registered { get; }
    }

    /// <summary>
    /// Describes a plugin-provided page factory along with its persisted type code.
    /// </summary>
    public sealed class PageFactoryRegistration
    {
        public PageFactoryRegistration(
            string pluginId,
            string pageType,
            byte numericCode,
            string compatibilityRange,
            Func<PageConstructionContext, object> factory)
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
            NumericCode = numericCode;
            CompatibilityRange = compatibilityRange ?? throw new ArgumentNullException(nameof(compatibilityRange));
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>
        /// Gets the plugin identifier associated with the registration.
        /// </summary>
        public string PluginId { get; }

        /// <summary>
        /// Gets the logical page type name.
        /// </summary>
        public string PageType { get; }

        /// <summary>
        /// Gets the numeric page code stored on disk.
        /// </summary>
        public byte NumericCode { get; }

        /// <summary>
        /// Gets the declared compatibility range for the page implementation.
        /// </summary>
        public string CompatibilityRange { get; }

        /// <summary>
        /// Gets the delegate responsible for constructing the page.
        /// </summary>
        public Func<PageConstructionContext, object> Factory { get; }
    }

    /// <summary>
    /// Context describing a page construction request.
    /// </summary>
    public sealed class PageConstructionContext
    {
        public PageConstructionContext(object buffer, uint pageId, bool isNewPage)
        {
            Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            PageId = pageId;
            IsNewPage = isNewPage;
        }

        /// <summary>
        /// Gets the buffer backing the request. Callers can cast this to engine-specific types when needed.
        /// </summary>
        public object Buffer { get; }

        /// <summary>
        /// Gets the page identifier associated with the buffer.
        /// </summary>
        public uint PageId { get; }

        /// <summary>
        /// Gets a value indicating whether the caller is constructing a new page.
        /// </summary>
        public bool IsNewPage { get; }
    }
}
