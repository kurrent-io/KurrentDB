#if FEATURE_UNLOAD
using System;

namespace McMaster.NETCore.Plugins
{
    /// <summary>
    /// Represents the method that will handle the <see cref="PluginLoader.Reloaded" /> event.
    /// </summary>
    /// <param name="sender">The object sending the event</param>
    /// <param name="eventArgs">Data about the event.</param>
    public delegate void PluginReloadedEventHandler(object sender, PluginReloadedEventArgs eventArgs);

    /// <summary>
    /// Provides data for the <see cref="PluginLoader.Reloaded" /> event.
    /// </summary>
    public class PluginReloadedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes <see cref="PluginReloadedEventArgs" />.
        /// </summary>
        /// <param name="loader"></param>
        public PluginReloadedEventArgs(PluginLoader loader)
        {
            Loader = loader;
        }

        /// <summary>
        /// The plugin loader
        /// </summary>
        public PluginLoader Loader { get; }
    }
}
#endif