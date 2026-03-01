using System;
using System.Collections.Generic;
using LiteDB.Plugins;

namespace LiteDB
{
    /// <summary>
    /// Provides configuration options for <see cref="LiteDatabase"/> construction.
    /// </summary>
    public sealed class LiteDatabaseOptions
    {
        /// <summary>
        /// Gets or sets the mapper instance that the database should use.
        /// Defaults to <see cref="BsonMapper.Global"/> when not specified.
        /// </summary>
        public BsonMapper Mapper { get; set; }

        /// <summary>
        /// Gets or sets the plugins that should be initialized for the database.
        /// </summary>
        public IEnumerable<ILitePlugin> Plugins { get; set; }

        /// <summary>
        /// Gets or sets the service provider exposed to plugins during initialization.
        /// Defaults to an empty provider when not specified.
        /// </summary>
        public IServiceProvider Services { get; set; }

        /// <summary>
        /// Gets or sets the logger implementation exposed to plugins during initialization.
        /// Defaults to a no-op logger when not specified.
        /// </summary>
        public ILogger Logger { get; set; }

        /// <summary>
        /// Gets or sets how the database should behave when plugin-owned assets are encountered but the owning plugin is missing.
        /// Defaults to <see cref="PluginMissingBehavior.RefuseDatabase"/>.
        /// </summary>
        public PluginMissingBehavior MissingPluginBehavior { get; set; } = PluginMissingBehavior.RefuseDatabase;

        /// <summary>
        /// Gets or sets whether plugins should be validated immediately when opening the database.
        /// When <c>null</c> (default), validation is disabled to preserve legacy behavior.
        /// </summary>
        public bool? ValidatePluginsOnOpen { get; set; }
    }
}
