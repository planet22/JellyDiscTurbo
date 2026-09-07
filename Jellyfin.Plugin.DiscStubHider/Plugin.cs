// <copyright file="Plugin.cs" company="Jellyfin Project">
// Copyright (c) Jellyfin Project. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.DiscStubHider
{
    using System;
    using System.Collections.Generic;
    using Jellyfin.Plugin.DiscStubHider.Configuration;
    using MediaBrowser.Common.Configuration;
    using MediaBrowser.Common.Plugins;
    using MediaBrowser.Controller.Plugins;
    using MediaBrowser.Model.Plugins;
    using MediaBrowser.Model.Serialization;

    /// <summary>
    /// The main entry point defining assembly identification metrics for Jellyfin.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of application path utilities.</param>
        /// <param name="xmlSerializer">Instance of data serialization framework utilities.</param>
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        /// <inheritdoc />
        public override string Name => "Disc Stub Hider";

        /// <inheritdoc />
        public override Guid Id => Guid.Parse("a5f8e6c2-421d-4eb7-bd0a-cf1176b92a54");

        /// <summary>
        /// Gets the current plugin instance reference.
        /// </summary>
        public static Plugin? Instance { get; private set; }

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "Disc Stub Hider",
                    EmbeddedResourcePath = GetType().Namespace + ".configPage.html"
                }
            };
        }
    }
}
