namespace LiteDB.Plugins
{
    /// <summary>
    /// Represents an engine component capable of storing a plugin context reference.
    /// </summary>
    internal interface IPluginContextHost
    {
        /// <summary>
        /// Assigns the plugin context that should be used by the host.
        /// </summary>
        /// <param name="context">The context that aggregates registered plugin services.</param>
        void SetPluginContext(ILitePluginContext context);
    }
}
