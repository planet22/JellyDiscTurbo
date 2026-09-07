// <copyright file="ServerEntryPoint.cs" company="Jellyfin Project">
// Copyright (c) Jellyfin Project. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.DiscStubHider
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Jellyfin.Plugin.DiscStubHider.Configuration;
    using MediaBrowser.Controller.Library;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Connects server ecosystem signals and physical directory watchers to manage physical disc stub placements.
    /// </summary>
    public sealed class ServerEntryPoint : IHostedService, IDisposable
    {
        /// <summary>
        /// How long to wait after the last filesystem event for a directory before evaluating it.
        /// Collapses bursts of Created/Deleted/Renamed events (e.g. extracting a season pack) into one pass.
        /// </summary>
        private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(750);

        private readonly ILibraryManager libraryManager;
        private readonly ILogger<ServerEntryPoint> logger;

        // One watcher per directory we're actively monitoring.
        private readonly ConcurrentDictionary<string, FileSystemWatcher> activeWatchers = new(StringComparer.OrdinalIgnoreCase);

        // Serializes StubEvaluator runs per-directory so overlapping events can't race on the same files
        // or fight over enabling/disabling the same watcher.
        private readonly ConcurrentDictionary<string, SemaphoreSlim> directoryLocks = new(StringComparer.OrdinalIgnoreCase);

        // Tracks the pending debounce timer per-directory so a new event can supersede an older, not-yet-run one.
        private readonly ConcurrentDictionary<string, CancellationTokenSource> pendingEvaluations = new(StringComparer.OrdinalIgnoreCase);

        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ServerEntryPoint"/> class.
        /// </summary>
        /// <param name="libraryManager">Instance of the internal management framework.</param>
        /// <param name="logger">Instance of the diagnostic logger utility.</param>
        public ServerEntryPoint(ILibraryManager libraryManager, ILogger<ServerEntryPoint> logger)
        {
            this.libraryManager = libraryManager;
            this.logger = logger;
        }

        /// <summary>
        /// Triggered when the application host is ready to start the service.
        /// </summary>
        /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            this.libraryManager.ItemAdded += this.OnLibraryChanged;
            this.libraryManager.ItemRemoved += this.OnLibraryChanged;
            this.libraryManager.ItemUpdated += this.OnLibraryChanged;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Triggered when the application host is performing a graceful shutdown.
        /// </summary>
        /// <param name="cancellationToken">Indicates that the shutdown process should no longer be graceful.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            this.CleanupResources();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Releases all resources managed by the entry point execution context.
        /// </summary>
        public void Dispose()
        {
            this.CleanupResources();
            GC.SuppressFinalize(this);
        }

        private void CleanupResources()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;

            this.libraryManager.ItemAdded -= this.OnLibraryChanged;
            this.libraryManager.ItemRemoved -= this.OnLibraryChanged;
            this.libraryManager.ItemUpdated -= this.OnLibraryChanged;

            foreach (var directory in this.activeWatchers.Keys)
            {
                this.RemoveWatcher(directory);
            }

            this.activeWatchers.Clear();
            this.directoryLocks.Clear();
            this.pendingEvaluations.Clear();
        }

        private void OnLibraryChanged(object? sender, ItemChangeEventArgs e)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || config.HandlingMode != StubHandlingMode.RenameToBackup)
            {
                return;
            }

            // Library events are independently gated from folder watching.
            if (!config.EnableLibraryEvents)
            {
                return;
            }

            var item = e.Item;
            if (item == null || item.IsFolder || string.IsNullOrEmpty(item.Path))
            {
                return;
            }

            string? directory = Path.GetDirectoryName(item.Path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            // Only create a watcher when folder watching is enabled.
            if (config.EnableFolderWatch)
            {
                this.EnsureWatcherCreated(directory);
            }

            string currentExtension = Path.GetExtension(item.Path);
            if (currentExtension.Equals(".disc", StringComparison.OrdinalIgnoreCase) || item.Path.EndsWith(".disc.bak", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            this.ScheduleEvaluation(directory);
        }

        private void EnsureWatcherCreated(string directory)
        {
            // Guard again in case config changed after the watcher path was reached.
            if (Plugin.Instance?.Configuration == null || !Plugin.Instance.Configuration.EnableFolderWatch)
            {
                return;
            }

            this.activeWatchers.GetOrAdd(directory, dir =>
            {
                var watcher = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, Filter = "*.*", EnableRaisingEvents = true };
                watcher.Created += this.OnFileSystemChanged;
                watcher.Deleted += this.OnFileSystemChanged;
                watcher.Renamed += this.OnFileSystemChanged;
                watcher.Error += this.OnWatcherError;
                return watcher;
            });
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || config.HandlingMode != StubHandlingMode.RenameToBackup)
            {
                return;
            }

            // Folder-watch events are independently gated from library events.
            if (!config.EnableFolderWatch)
            {
                return;
            }

            string? directory = Path.GetDirectoryName(e.FullPath);
            if (string.IsNullOrEmpty(directory) || Path.GetExtension(e.FullPath).Equals(".bak", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            this.ScheduleEvaluation(directory);
        }

        /// <summary>
        /// A <see cref="FileSystemWatcher"/> raises Error when its internal event buffer overflows or the
        /// watched directory disappears out from under it. Either way the watcher is no longer trustworthy,
        /// so we tear it down instead of letting it sit around silently dead.
        /// </summary>
        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            string? directory = (sender as FileSystemWatcher)?.Path;
            this.logger.LogWarning(e.GetException(), "FileSystemWatcher error for {Dir}; removing watcher.", directory);

            if (!string.IsNullOrEmpty(directory))
            {
                this.RemoveWatcher(directory);
            }
        }

        /// <summary>
        /// Debounces bursts of filesystem events for the same directory into a single evaluation, then
        /// runs it under the per-directory lock.
        /// </summary>
        private void ScheduleEvaluation(string directory)
        {
            var cts = new CancellationTokenSource();

            // Cancel any previously-pending, not-yet-fired evaluation for this directory and replace it with ours.
            this.pendingEvaluations.AddOrUpdate(
                directory,
                cts,
                (_, existing) =>
                {
                    existing.Cancel();
                    existing.Dispose();
                    return cts;
                });

            var token = cts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DebounceDelay, token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    // Superseded by a newer event for the same directory; the newer scheduled call will run instead.
                    return;
                }

                // Only clear the pending entry if it's still the one we scheduled (a newer one may have replaced it
                // in the (unlikely) window between the delay completing and this line).
                this.pendingEvaluations.TryRemove(new KeyValuePair<string, CancellationTokenSource>(directory, cts));

                await this.EvaluateDirectorySafeAsync(directory).ConfigureAwait(false);
            });
        }

        /// <summary>
        /// Runs <see cref="StubEvaluator.EvaluateDirectory"/> for a single directory, serialized against any other
        /// in-flight evaluation of the same directory, with the directory's watcher suppressed for the duration
        /// so our own file moves don't re-trigger this same pipeline.
        /// </summary>
        private async Task EvaluateDirectorySafeAsync(string directory)
        {
            var gate = this.directoryLocks.GetOrAdd(directory, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (this.activeWatchers.TryGetValue(directory, out var watcher))
                {
                    watcher.EnableRaisingEvents = false;
                }

                StubEvaluator.EvaluateDirectory(directory, this.logger);

                if (!Directory.Exists(directory))
                {
                    // The directory itself is gone (e.g. the item/folder was deleted). No point keeping a
                    // watcher, lock, or pending-evaluation entry around for it.
                    this.RemoveWatcher(directory);
                    return;
                }

                if (this.activeWatchers.TryGetValue(directory, out watcher))
                {
                    watcher.EnableRaisingEvents = true;
                }
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Fully removes and disposes tracking state for a directory: the watcher itself, its handlers,
        /// its per-directory lock, and any pending debounce timer. Called both on shutdown and whenever
        /// we discover a watched directory no longer exists or its watcher has errored out.
        /// </summary>
        private void RemoveWatcher(string directory)
        {
            if (this.activeWatchers.TryRemove(directory, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Created -= this.OnFileSystemChanged;
                watcher.Deleted -= this.OnFileSystemChanged;
                watcher.Renamed -= this.OnFileSystemChanged;
                watcher.Error -= this.OnWatcherError;
                watcher.Dispose();
            }

            if (this.directoryLocks.TryRemove(directory, out var gate))
            {
                gate.Dispose();
            }

            if (this.pendingEvaluations.TryRemove(directory, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
    }
}
