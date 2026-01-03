using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YoutubeDownloader.Core.Downloading;

public static class FFmpeg
{
    private static string CliFileName { get; } =
        OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    public static string? TryGetCliFilePath()
    {
        static IEnumerable<string> GetProbeDirectoryPaths()
        {
            // Check ffmpeg folder at the same level as the application
            yield return Path.Combine(AppContext.BaseDirectory, "ffmpeg");

            // Check ffmpeg folder at the parent directory (same level as project)
            var parentDir = Directory.GetParent(AppContext.BaseDirectory)?.FullName;
            if (parentDir != null)
                yield return Path.Combine(parentDir, "ffmpeg");

            yield return AppContext.BaseDirectory;
            yield return Directory.GetCurrentDirectory();

            // Check ffmpeg folder at the same level as current directory
            yield return Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg");

            // Process PATH
            if (
                Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) is
                { } processPaths
            )
            {
                foreach (var path in processPaths)
                    yield return path;
            }

            // Registry-based PATH variables
            if (OperatingSystem.IsWindows())
            {
                // User PATH
                if (
                    Environment
                        .GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)
                        ?.Split(Path.PathSeparator) is
                    { } userPaths
                )
                {
                    foreach (var path in userPaths)
                        yield return path;
                }

                // System PATH
                if (
                    Environment
                        .GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)
                        ?.Split(Path.PathSeparator) is
                    { } systemPaths
                )
                {
                    foreach (var path in systemPaths)
                        yield return path;
                }
            }
        }

        return GetProbeDirectoryPaths()
            .Distinct(StringComparer.Ordinal)
            .Select(dirPath => Path.Combine(dirPath, CliFileName))
            .FirstOrDefault(File.Exists);
    }

    public static bool IsBundled() =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, CliFileName))
        || File.Exists(Path.Combine(AppContext.BaseDirectory, "ffmpeg", CliFileName));

    public static bool IsAvailable() => !string.IsNullOrWhiteSpace(TryGetCliFilePath());
}
