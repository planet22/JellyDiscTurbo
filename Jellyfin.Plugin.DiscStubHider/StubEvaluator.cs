// <copyright file="StubEvaluator.cs" company="Jellyfin Project">
// Copyright (c) Jellyfin Project. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.DiscStubHider
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Shared business logic utility handling filesystem evaluation and renaming mechanics for disc stub assets.
    /// </summary>
    /// <remarks>
    /// This type is intentionally "dumb": it just looks at a directory and moves files. Concerns like
    /// suppressing <see cref="FileSystemWatcher"/> events while a move is in flight, or preventing two
    /// evaluations of the same directory from racing each other, are the caller's responsibility
    /// (see <c>ServerEntryPoint</c>, which serializes calls per-directory).
    /// </remarks>
    public static partial class StubEvaluator
    {
        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".strm", ".mkv", ".mp4", ".avi", ".mov", ".wmv"
        };

        /// <summary>
        /// Checks a local folder and shifts .disc file extension patterns depending on companion asset drops.
        /// </summary>
        /// <param name="directory">The target directory being monitored or cataloged.</param>
        /// <param name="logger">Instance of the logging diagnostic contract asset.</param>
        public static void EvaluateDirectory(string directory, ILogger logger)
        {
            try
            {
                bool hasVideoFile = Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                    .Any(file => VideoExtensions.Contains(Path.GetExtension(file)));

                if (hasVideoFile)
                {
                    var activeStubs = Directory.GetFiles(directory, "*.disc");
                    foreach (var stubPath in activeStubs)
                    {
                        string targetPath = stubPath + ".bak";

                        if (File.Exists(targetPath))
                        {
                            logger.LogWarning(
                                "Backup stub {Target} already exists in {Dir} and will be overwritten.",
                                Path.GetFileName(targetPath),
                                directory);
                        }

                        string stubFileName = Path.GetFileName(stubPath);
                        LogStubHidden(logger, directory, stubFileName);
                        File.Move(stubPath, targetPath, overwrite: true);
                    }
                }
                else
                {
                    var hiddenStubs = Directory.GetFiles(directory, "*.disc.bak");
                    foreach (var backupPath in hiddenStubs)
                    {
                        string targetPath = backupPath[..^4];

                        if (File.Exists(targetPath))
                        {
                            logger.LogWarning(
                                "Stub {Target} already exists in {Dir} and will be overwritten.",
                                Path.GetFileName(targetPath),
                                directory);
                        }

                        string backupFileName = Path.GetFileName(backupPath);
                        LogStubRestored(logger, directory, backupFileName);
                        File.Move(backupPath, targetPath, overwrite: true);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing stub file adjustments in directory: {Directory}", directory);
            }
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "Video file found in {Dir}. Renaming stub {File} -> .disc.bak")]
        private static partial void LogStubHidden(ILogger logger, string dir, string file);

        [LoggerMessage(Level = LogLevel.Information, Message = "No video files left in {Dir}. Restoring stub {File} -> .disc")]
        private static partial void LogStubRestored(ILogger logger, string dir, string file);
    }
}
