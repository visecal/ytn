using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Gress;

namespace YoutubeDownloader.Core.Downloading;

/// <summary>
/// Options for copyright avoidance processing
/// </summary>
public class CopyrightAvoidanceOptions
{
    /// <summary>
    /// Apply horizontal flip to the video
    /// </summary>
    public bool HorizontalFlip { get; set; }

    /// <summary>
    /// Apply vertical flip to the video
    /// </summary>
    public bool VerticalFlip { get; set; }

    /// <summary>
    /// Scale factor for video (e.g., 0.95 = 95% of original size)
    /// </summary>
    public double? ScaleFactor { get; set; }

    /// <summary>
    /// Audio pitch shift factor (e.g., 1.05 = 5% higher pitch, 0.95 = 5% lower pitch)
    /// </summary>
    public double? AudioPitchFactor { get; set; }

    /// <summary>
    /// Speed factor (e.g., 1.05 = 5% faster, 0.95 = 5% slower)
    /// </summary>
    public double? SpeedFactor { get; set; }

    /// <summary>
    /// Randomly reverse some frames
    /// </summary>
    public bool RandomFrameReverse { get; set; }

    /// <summary>
    /// Add slight rotation (in degrees, e.g., 1-2 degrees)
    /// </summary>
    public double? RotationDegrees { get; set; }

    /// <summary>
    /// Change brightness slightly (e.g., 0.05 = 5% brighter, -0.05 = 5% darker)
    /// </summary>
    public double? BrightnessAdjust { get; set; }

    /// <summary>
    /// Change contrast slightly (e.g., 1.05 = 5% more contrast)
    /// </summary>
    public double? ContrastAdjust { get; set; }

    /// <summary>
    /// Add slight blur (sigma value, e.g., 0.5)
    /// </summary>
    public double? BlurSigma { get; set; }
}

/// <summary>
/// Options for merging voice into video
/// </summary>
public class VoiceMergeOptions
{
    /// <summary>
    /// Path to the voice/audio file to merge
    /// </summary>
    public string VoiceFilePath { get; set; } = "";

    /// <summary>
    /// Whether to mute the original video audio
    /// </summary>
    public bool MuteOriginalAudio { get; set; }

    /// <summary>
    /// Volume of the voice (0.0 to 2.0, where 1.0 is normal)
    /// </summary>
    public double VoiceVolume { get; set; } = 1.0;

    /// <summary>
    /// Volume of the original audio when not muted (0.0 to 2.0, where 1.0 is normal)
    /// </summary>
    public double OriginalAudioVolume { get; set; } = 0.3;
}

/// <summary>
/// Service for processing videos with FFmpeg
/// </summary>
public class VideoProcessor
{
    private static string GetFFmpegPath() =>
        FFmpeg.TryGetCliFilePath() ?? throw new InvalidOperationException("FFmpeg not found");

    /// <summary>
    /// Extract audio from a video file
    /// </summary>
    public async Task ExtractAudioAsync(
        string videoPath,
        string outputAudioPath,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var ffmpegPath = GetFFmpegPath();

        var arguments =
            $"-i \"{videoPath}\" -vn -acodec libmp3lame -q:a 2 -y \"{outputAudioPath}\"";

        await RunFFmpegAsync(ffmpegPath, arguments, progress, cancellationToken);
    }

    /// <summary>
    /// Apply copyright avoidance techniques to a video
    /// </summary>
    public async Task ProcessForCopyrightAvoidanceAsync(
        string inputPath,
        string outputPath,
        CopyrightAvoidanceOptions options,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var ffmpegPath = GetFFmpegPath();

        var videoFilters = new System.Collections.Generic.List<string>();
        var audioFilters = new System.Collections.Generic.List<string>();

        // Video filters
        if (options.HorizontalFlip)
            videoFilters.Add("hflip");

        if (options.VerticalFlip)
            videoFilters.Add("vflip");

        if (options.ScaleFactor.HasValue)
        {
            var scale = options.ScaleFactor.Value;
            videoFilters.Add($"scale=iw*{scale:F2}:ih*{scale:F2}");
        }

        if (options.RotationDegrees.HasValue)
        {
            var radians = options.RotationDegrees.Value * Math.PI / 180;
            videoFilters.Add($"rotate={radians:F4}");
        }

        if (options.BrightnessAdjust.HasValue || options.ContrastAdjust.HasValue)
        {
            var brightness = options.BrightnessAdjust ?? 0;
            var contrast = options.ContrastAdjust ?? 1;
            videoFilters.Add($"eq=brightness={brightness:F2}:contrast={contrast:F2}");
        }

        if (options.BlurSigma.HasValue)
        {
            videoFilters.Add($"gblur=sigma={options.BlurSigma.Value:F1}");
        }

        if (options.SpeedFactor.HasValue)
        {
            var speed = options.SpeedFactor.Value;
            videoFilters.Add($"setpts={1 / speed:F3}*PTS");
        }

        // Audio filters
        if (options.AudioPitchFactor.HasValue)
        {
            var pitch = options.AudioPitchFactor.Value;
            audioFilters.Add($"asetrate=44100*{pitch:F2},aresample=44100");
        }

        if (options.SpeedFactor.HasValue)
        {
            var speed = options.SpeedFactor.Value;
            audioFilters.Add($"atempo={speed:F3}");
        }

        var filterArgs = "";
        if (videoFilters.Count > 0)
            filterArgs += $"-vf \"{string.Join(",", videoFilters)}\"";

        if (audioFilters.Count > 0)
        {
            if (!string.IsNullOrEmpty(filterArgs))
                filterArgs += " ";
            filterArgs += $"-af \"{string.Join(",", audioFilters)}\"";
        }

        var arguments =
            $"-i \"{inputPath}\" {filterArgs} -c:v libx264 -preset medium -crf 23 -c:a aac -b:a 192k -y \"{outputPath}\"";

        await RunFFmpegAsync(ffmpegPath, arguments, progress, cancellationToken);
    }

    /// <summary>
    /// Merge voice/audio into video
    /// </summary>
    public async Task MergeVoiceIntoVideoAsync(
        string videoPath,
        string outputPath,
        VoiceMergeOptions options,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var ffmpegPath = GetFFmpegPath();

        string arguments;
        if (options.MuteOriginalAudio)
        {
            // Mute original audio and use only the voice
            arguments =
                $"-i \"{videoPath}\" -i \"{options.VoiceFilePath}\" "
                + $"-filter_complex \"[1:a]volume={options.VoiceVolume:F2}[voice]\" "
                + $"-map 0:v -map \"[voice]\" -c:v copy -c:a aac -b:a 192k -shortest -y \"{outputPath}\"";
        }
        else
        {
            // Mix original audio with voice
            arguments =
                $"-i \"{videoPath}\" -i \"{options.VoiceFilePath}\" "
                + $"-filter_complex \"[0:a]volume={options.OriginalAudioVolume:F2}[a0];[1:a]volume={options.VoiceVolume:F2}[a1];[a0][a1]amix=inputs=2:duration=first[aout]\" "
                + $"-map 0:v -map \"[aout]\" -c:v copy -c:a aac -b:a 192k -y \"{outputPath}\"";
        }

        await RunFFmpegAsync(ffmpegPath, arguments, progress, cancellationToken);
    }

    /// <summary>
    /// Batch merge voice files into videos based on index prefix
    /// </summary>
    public async Task BatchMergeVoiceAsync(
        string videoDirectory,
        string voiceDirectory,
        string outputDirectory,
        bool muteOriginalAudio,
        double voiceVolume = 1.0,
        double originalAudioVolume = 0.3,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        Directory.CreateDirectory(outputDirectory);

        var videoFiles = Directory
            .GetFiles(videoDirectory, "*.*")
            .Where(f => IsVideoFile(f))
            .OrderBy(f => f)
            .ToArray();

        var voiceFiles = Directory
            .GetFiles(voiceDirectory, "*.*")
            .Where(f => IsAudioFile(f))
            .OrderBy(f => f)
            .ToArray();

        var totalFiles = Math.Min(videoFiles.Length, voiceFiles.Length);

        for (var i = 0; i < totalFiles; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var videoFile = videoFiles[i];
            var voiceFile = voiceFiles[i];

            // Extract index from video filename (format: index_videoname.ext)
            var videoFileName = Path.GetFileName(videoFile);
            var outputFileName = videoFileName;

            var outputPath = Path.Combine(outputDirectory, outputFileName);

            var options = new VoiceMergeOptions
            {
                VoiceFilePath = voiceFile,
                MuteOriginalAudio = muteOriginalAudio,
                VoiceVolume = voiceVolume,
                OriginalAudioVolume = originalAudioVolume,
            };

            await MergeVoiceIntoVideoAsync(videoFile, outputPath, options, null, cancellationToken);

            progress?.Report(Percentage.FromFraction((i + 1.0) / totalFiles));
        }
    }

    private static bool IsVideoFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp4" or ".webm" or ".mkv" or ".avi" or ".mov" or ".flv";
    }

    private static bool IsAudioFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp3" or ".wav" or ".aac" or ".ogg" or ".m4a" or ".flac";
    }

    private static async Task RunFFmpegAsync(
        string ffmpegPath,
        string arguments,
        IProgress<Percentage>? progress,
        CancellationToken cancellationToken
    )
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        var duration = TimeSpan.Zero;

        process.ErrorDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            // Parse duration from FFmpeg output
            if (e.Data.Contains("Duration:"))
            {
                var durationMatch = Regex.Match(
                    e.Data,
                    @"Duration: (\d{2}):(\d{2}):(\d{2})\.(\d{2})"
                );
                if (durationMatch.Success)
                {
                    duration = new TimeSpan(
                        0,
                        int.Parse(durationMatch.Groups[1].Value),
                        int.Parse(durationMatch.Groups[2].Value),
                        int.Parse(durationMatch.Groups[3].Value),
                        int.Parse(durationMatch.Groups[4].Value) * 10
                    );
                }
            }

            // Parse progress from FFmpeg output
            if (e.Data.Contains("time=") && duration > TimeSpan.Zero)
            {
                var timeMatch = Regex.Match(e.Data, @"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                if (timeMatch.Success)
                {
                    var currentTime = new TimeSpan(
                        0,
                        int.Parse(timeMatch.Groups[1].Value),
                        int.Parse(timeMatch.Groups[2].Value),
                        int.Parse(timeMatch.Groups[3].Value),
                        int.Parse(timeMatch.Groups[4].Value) * 10
                    );
                    var progressValue = currentTime.TotalMilliseconds / duration.TotalMilliseconds;
                    progress?.Report(Percentage.FromFraction(Math.Min(progressValue, 1.0)));
                }
            }
        };

        process.Start();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill();
            }
            catch { }
        });

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"FFmpeg process exited with code {process.ExitCode}"
            );

        progress?.Report(Percentage.FromFraction(1.0));
    }
}
