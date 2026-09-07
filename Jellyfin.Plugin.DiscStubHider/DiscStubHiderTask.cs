// <copyright file="DiscStubHiderTask.cs" company="Jellyfin Project">
// Copyright (c) Jellyfin Project. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.DiscStubHider
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Jellyfin.Data.Enums; // Required for BaseItemKind enum parsing maps
    using Jellyfin.Plugin.DiscStubHider.Configuration;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Model.Tasks;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Implements a scheduled background process to scan libraries for active or hidden disk stubs.
    /// </summary>
    public sealed class DiscStubHiderTask : IScheduledTask
    {
        private readonly ILibraryManager libraryManager;
        private readonly ILogger<DiscStubHiderTask> logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="DiscStubHiderTask"/> class.
        /// </summary>
        /// <param name="libraryManager">Instance of the framework library management infrastructure tool.</param>
        /// <param name="logger">Instance of the logging diagnostic interface pool.</param>
        public DiscStubHiderTask(ILibraryManager libraryManager, ILogger<DiscStubHiderTask> logger)
        {
            this.libraryManager = libraryManager;
            this.logger = logger;
        }

        /// <inheritdoc />
        public string Name => "Scan and Hide Disc Stubs";

        /// <inheritdoc />
        public string Key => "DiscStubHiderScanTask";

        /// <inheritdoc />
        public string Description => "Manually scans all library paths to update, rename, or restore physical disk stubs.";

        /// <inheritdoc />
        public string Category => "Library";

        /// <inheritdoc />
        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (Plugin.Instance?.Configuration == null || Plugin.Instance.Configuration.HandlingMode != StubHandlingMode.RenameToBackup)
            {
                this.logger.LogWarning("Task execution aborted: Plugin configuration is disabled or mode is set to ignore.");
                progress?.Report(100);
                return Task.CompletedTask;
            }

            // Explicitly filter types using structural enums to prevent database deserialization faults
            var query = new InternalItemsQuery
            {
                IsFolder = false,
                Recursive = true,
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.MusicVideo }
            };

            var items = this.libraryManager.GetItemList(query);
            if (items == null)
            {
                progress?.Report(100);
                return Task.CompletedTask;
            }

            var targetDirectories = items
                .Where(i => i != null && !string.IsNullOrEmpty(i.Path))
                .Select(i => Path.GetDirectoryName(i.Path))
                .Where(dir => !string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            double total = targetDirectories.Count;
            if (total == 0)
            {
                progress?.Report(100);
                return Task.CompletedTask;
            }

            for (int i = 0; i < targetDirectories.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string currentDir = targetDirectories[i];
                StubEvaluator.EvaluateDirectory(currentDir, this.logger);

                double percentComplete = ((double)(i + 1) / total) * 100;
                progress?.Report(percentComplete);
            }

            progress?.Report(100);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.WeeklyTrigger,
                    DayOfWeek = DayOfWeek.Sunday,
                    TimeOfDayTicks = TimeSpan.FromHours(2).Ticks
                }
            };
        }
    }
}
