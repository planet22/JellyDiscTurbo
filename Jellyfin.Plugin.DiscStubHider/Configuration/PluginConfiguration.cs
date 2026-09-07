// <copyright file="PluginConfiguration.cs" company="Jellyfin Project">
// Copyright (c) Jellyfin Project. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.DiscStubHider.Configuration
{
    using MediaBrowser.Model.Plugins;

    /// <summary>
    /// The operational modes for handling disc stub files.
    /// </summary>
    public enum StubHandlingMode
    {
        /// <summary>
        /// Rename the .disc file to .disc.bak when a video file exists.
        /// </summary>
        RenameToBackup,

        /// <summary>
        /// Completely bypass processing strategies when assets match.
        /// </summary>
        Ignore,
    }

    /// <summary>
    /// Plugin configuration options for the Disc Stub Hider.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
        /// </summary>
        public PluginConfiguration()
        {
            // Set default production options
            this.HandlingMode = StubHandlingMode.RenameToBackup;
            this.EnableLibraryEvents = true;
            this.EnableFolderWatch = true;
        }

        /// <summary>
        /// Gets or sets a value indicating whether library item events
        /// (ItemAdded / ItemRemoved / ItemUpdated) should trigger processing.
        /// When disabled, library changes alone will not run stub evaluation.
        /// </summary>
        public bool EnableLibraryEvents { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether FileSystemWatcher-based
        /// folder watching should be used. When disabled, no per-directory
        /// watchers are created and filesystem change events are ignored.
        /// </summary>
        public bool EnableFolderWatch { get; set; }

        /// <summary>
        /// Gets or sets the preferred strategy for managing detected disc stubs.
        /// </summary>
        public StubHandlingMode HandlingMode { get; set; }
    }
}
