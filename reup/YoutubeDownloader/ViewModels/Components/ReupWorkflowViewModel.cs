using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gress;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Core.Resolving;
using YoutubeDownloader.Core.Uploading;
using YoutubeDownloader.Framework;
using YoutubeDownloader.Services;

namespace YoutubeDownloader.ViewModels.Components;

public partial class ReupWorkflowViewModel : ViewModelBase
{
    private readonly DialogManager _dialogManager;
    private readonly SettingsService _settingsService;
    private CancellationTokenSource? _cancellationTokenSource;
    private YouTubeUploader? _uploader;

    public ReupWorkflowViewModel(DialogManager dialogManager, SettingsService settingsService)
    {
        _dialogManager = dialogManager;
        _settingsService = settingsService;

        Categories = Enum.GetValues<VideoCategory>().Select(c => c.ToString()).ToArray();
        PrivacyOptions = Enum.GetValues<VideoPrivacyStatus>().Select(p => p.ToString()).ToArray();
        QualityOptions = ["Highest", "1080p", "720p", "480p", "360p", "Lowest"];

        SelectedCategory = "Entertainment";
        SelectedPrivacy = "Private";
        SelectedQuality = "1080p";
    }

    // Step tracking
    [ObservableProperty]
    public partial int CurrentStep { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotProcessing))]
    public partial bool IsProcessing { get; set; }

    public bool IsNotProcessing => !IsProcessing;

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public ProgressContainer<Percentage> Progress { get; } = new();

    // Step 1: Source
    [ObservableProperty]
    public partial string? SourceUrl { get; set; }

    [ObservableProperty]
    public partial bool IsSingleVideo { get; set; } = true;

    [ObservableProperty]
    public partial bool IsPlaylist { get; set; }

    [ObservableProperty]
    public partial bool IsChannel { get; set; }

    [ObservableProperty]
    public partial string? SelectedQuality { get; set; }

    [ObservableProperty]
    public partial int MaxVideos { get; set; } = 50;

    [ObservableProperty]
    public partial string? DownloadFolder { get; set; }

    public string[] QualityOptions { get; }

    // Step 2: Encoding
    [ObservableProperty]
    public partial bool EnableEncoding { get; set; } = true;

    [ObservableProperty]
    public partial bool UseLightPreset { get; set; } = true;

    [ObservableProperty]
    public partial bool UseMediumPreset { get; set; }

    [ObservableProperty]
    public partial bool UseHeavyPreset { get; set; }

    [ObservableProperty]
    public partial bool EncodeHorizontalFlip { get; set; }

    [ObservableProperty]
    public partial bool EncodePitchShift { get; set; } = true;

    [ObservableProperty]
    public partial bool EncodeSpeedChange { get; set; }

    [ObservableProperty]
    public partial bool EncodeBrightness { get; set; }

    // Step 3: Upload
    [ObservableProperty]
    public partial bool EnableUpload { get; set; } = true;

    [ObservableProperty]
    public partial string? ServiceAccountPath { get; set; }

    [ObservableProperty]
    public partial bool IsServiceAccountValid { get; set; }

    [ObservableProperty]
    public partial string TitleTemplate { get; set; } = "{original}";

    [ObservableProperty]
    public partial string DescriptionTemplate { get; set; } = "";

    [ObservableProperty]
    public partial string Tags { get; set; } = "";

    [ObservableProperty]
    public partial string? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial string? SelectedPrivacy { get; set; }

    [ObservableProperty]
    public partial int UploadDelaySeconds { get; set; } = 30;

    public string[] Categories { get; }
    public string[] PrivacyOptions { get; }

    public ObservableCollection<string> ProcessingLog { get; } = [];

    [RelayCommand]
    private async Task SelectDownloadFolderAsync()
    {
        var folderPath = await _dialogManager.PromptDirectoryPathAsync();

        if (!string.IsNullOrWhiteSpace(folderPath))
            DownloadFolder = folderPath;
    }

    [RelayCommand]
    private async Task SelectServiceAccountAsync()
    {
        var filePath = await _dialogManager.PromptOpenFilePathAsync(
            [
                new FilePickerFileType("JSON files")
                {
                    Patterns = ["*.json"],
                },
            ]
        );

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            ServiceAccountPath = filePath;

            try
            {
                _uploader?.Dispose();
                _uploader = new YouTubeUploader(ServiceAccountPath);
                IsServiceAccountValid = await _uploader.VerifyConnectionAsync();

                if (IsServiceAccountValid)
                {
                    var channel = await _uploader.GetChannelInfoAsync();
                    AddLog($"Connected to YouTube channel: {channel?.Snippet?.Title}");
                }
                else
                {
                    AddLog("Failed to connect to YouTube. Check Service Account permissions.");
                }
            }
            catch (Exception ex)
            {
                IsServiceAccountValid = false;
                AddLog($"Service Account error: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task PreviewWorkflowAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceUrl))
        {
            StatusMessage = "Please enter a source URL";
            return;
        }

        ProcessingLog.Clear();
        AddLog("=== Workflow Preview ===");

        try
        {
            using var resolver = new QueryResolver(_settingsService.LastAuthCookies);
            var result = await resolver.ResolveAsync(SourceUrl);

            AddLog($"Source type: {result.Kind}");
            AddLog($"Found {result.Videos.Count} video(s)");

            if (result.Videos.Count > 0)
            {
                var videosToProcess = result.Videos.Take(MaxVideos).ToList();
                AddLog($"Will process {videosToProcess.Count} video(s) (max: {MaxVideos})");

                foreach (var (index, video) in videosToProcess.Select((v, i) => (i, v)).Take(5))
                {
                    AddLog($"  {index + 1}. {video.Title}");
                }

                if (videosToProcess.Count > 5)
                    AddLog($"  ... and {videosToProcess.Count - 5} more");
            }

            if (EnableEncoding)
            {
                AddLog("");
                AddLog("Encoding: ENABLED");
                AddLog($"  - Preset: {(UseLightPreset ? "Light" : UseMediumPreset ? "Medium" : "Heavy")}");
            }
            else
            {
                AddLog("Encoding: DISABLED");
            }

            if (EnableUpload)
            {
                AddLog("");
                AddLog("Upload: ENABLED");
                AddLog($"  - Title template: {TitleTemplate}");
                AddLog($"  - Privacy: {SelectedPrivacy}");
                AddLog($"  - Delay between uploads: {UploadDelaySeconds}s");
            }
            else
            {
                AddLog("Upload: DISABLED");
            }

            StatusMessage = "Preview complete. Ready to start workflow.";
        }
        catch (Exception ex)
        {
            AddLog($"Error: {ex.Message}");
            StatusMessage = $"Preview failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StartWorkflowAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceUrl))
        {
            StatusMessage = "Please enter a source URL";
            return;
        }

        if (string.IsNullOrWhiteSpace(DownloadFolder))
        {
            DownloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "YouTubeReup");
            Directory.CreateDirectory(DownloadFolder);
        }

        if (EnableUpload && !IsServiceAccountValid)
        {
            StatusMessage = "Please configure a valid Service Account for uploading";
            return;
        }

        IsProcessing = true;
        _cancellationTokenSource = new CancellationTokenSource();
        ProcessingLog.Clear();

        try
        {
            // Step 1: Download
            CurrentStep = 1;
            AddLog("=== Step 1: Downloading Videos ===");
            var downloadedFiles = await DownloadVideosAsync();

            if (downloadedFiles.Length == 0)
            {
                AddLog("No videos downloaded. Workflow stopped.");
                StatusMessage = "No videos were downloaded";
                return;
            }

            AddLog($"Downloaded {downloadedFiles.Length} video(s)");

            // Step 2: Encode
            string[] processedFiles;
            if (EnableEncoding)
            {
                CurrentStep = 2;
                AddLog("");
                AddLog("=== Step 2: Encoding Videos ===");
                processedFiles = await EncodeVideosAsync(downloadedFiles);
                AddLog($"Encoded {processedFiles.Length} video(s)");
            }
            else
            {
                processedFiles = downloadedFiles;
                AddLog("Encoding skipped (disabled)");
            }

            // Step 3: Upload
            if (EnableUpload)
            {
                CurrentStep = 3;
                AddLog("");
                AddLog("=== Step 3: Uploading Videos ===");
                await UploadVideosAsync(processedFiles);
            }
            else
            {
                AddLog("Upload skipped (disabled)");
            }

            AddLog("");
            AddLog("=== Workflow Completed ===");
            StatusMessage = "Reup workflow completed successfully!";
        }
        catch (OperationCanceledException)
        {
            AddLog("Workflow cancelled by user");
            StatusMessage = "Workflow cancelled";
        }
        catch (Exception ex)
        {
            AddLog($"Error: {ex.Message}");
            StatusMessage = $"Workflow failed: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            CurrentStep = 1;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private async Task<string[]> DownloadVideosAsync()
    {
        using var resolver = new QueryResolver(_settingsService.LastAuthCookies);
        var result = await resolver.ResolveAsync(SourceUrl!);

        var videosToDownload = result.Videos.Take(MaxVideos).ToList();
        var downloadedFiles = new System.Collections.Generic.List<string>();

        using var downloader = new VideoDownloader(_settingsService.LastAuthCookies);
        var qualityPref = GetQualityPreference();

        foreach (var (index, video) in videosToDownload.Select((v, i) => (i, v)))
        {
            _cancellationTokenSource!.Token.ThrowIfCancellationRequested();

            StatusMessage = $"Downloading {index + 1}/{videosToDownload.Count}: {video.Title}";
            AddLog($"Downloading: {video.Title}");

            try
            {
                var downloadOption = await downloader.GetBestDownloadOptionAsync(
                    video.Id,
                    new VideoDownloadPreference(Container.Mp4, qualityPref),
                    false,
                    _cancellationTokenSource.Token
                );

                var safeFileName = SanitizeFileName(video.Title);
                var filePath = Path.Combine(DownloadFolder!, $"{index + 1:D3}_{safeFileName}.mp4");

                await downloader.DownloadVideoAsync(
                    filePath,
                    video,
                    downloadOption,
                    false,
                    Progress,
                    _cancellationTokenSource.Token
                );

                downloadedFiles.Add(filePath);
                AddLog($"  Saved to: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                AddLog($"  Failed: {ex.Message}");
            }

            Progress.Report(Percentage.FromFraction((index + 1.0) / videosToDownload.Count));
        }

        return [.. downloadedFiles];
    }

    private async Task<string[]> EncodeVideosAsync(string[] inputFiles)
    {
        var processor = new VideoProcessor();
        var options = BuildEncodingOptions();
        var encodedFolder = Path.Combine(DownloadFolder!, "encoded");
        Directory.CreateDirectory(encodedFolder);

        var encodedFiles = new System.Collections.Generic.List<string>();

        foreach (var (index, inputFile) in inputFiles.Select((f, i) => (i, f)))
        {
            _cancellationTokenSource!.Token.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(inputFile);
            var outputFile = Path.Combine(encodedFolder, fileName);

            StatusMessage = $"Encoding {index + 1}/{inputFiles.Length}: {fileName}";
            AddLog($"Encoding: {fileName}");

            try
            {
                await processor.ProcessForCopyrightAvoidanceAsync(
                    inputFile,
                    outputFile,
                    options,
                    Progress,
                    _cancellationTokenSource.Token
                );

                encodedFiles.Add(outputFile);
                AddLog($"  Encoded successfully");
            }
            catch (Exception ex)
            {
                AddLog($"  Encoding failed: {ex.Message}");
                // Use original file if encoding fails
                encodedFiles.Add(inputFile);
            }

            Progress.Report(Percentage.FromFraction((index + 1.0) / inputFiles.Length));
        }

        return [.. encodedFiles];
    }

    private async Task UploadVideosAsync(string[] files)
    {
        if (_uploader == null)
            return;

        foreach (var (index, file) in files.Select((f, i) => (i, f)))
        {
            _cancellationTokenSource!.Token.ThrowIfCancellationRequested();

            var fileName = Path.GetFileNameWithoutExtension(file);
            var title = TitleTemplate
                .Replace("{original}", fileName)
                .Replace("{index}", (index + 1).ToString());

            StatusMessage = $"Uploading {index + 1}/{files.Length}: {title}";
            AddLog($"Uploading: {title}");

            try
            {
                var options = new VideoUploadOptions
                {
                    Title = title,
                    Description = DescriptionTemplate,
                    Tags = Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    Category = Enum.TryParse<VideoCategory>(SelectedCategory, out var cat) ? cat : VideoCategory.Entertainment,
                    PrivacyStatus = Enum.TryParse<VideoPrivacyStatus>(SelectedPrivacy, out var priv) ? priv : VideoPrivacyStatus.Private,
                    NotifySubscribers = true,
                    MadeForKids = false
                };

                var result = await _uploader.UploadVideoAsync(
                    file,
                    options,
                    Progress,
                    _cancellationTokenSource.Token
                );

                if (result.Success)
                {
                    AddLog($"  Uploaded: {result.VideoUrl}");
                }
                else
                {
                    AddLog($"  Upload failed: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                AddLog($"  Upload error: {ex.Message}");
            }

            // Delay between uploads
            if (index < files.Length - 1 && UploadDelaySeconds > 0)
            {
                AddLog($"  Waiting {UploadDelaySeconds}s before next upload...");
                await Task.Delay(TimeSpan.FromSeconds(UploadDelaySeconds), _cancellationTokenSource.Token);
            }

            Progress.Report(Percentage.FromFraction((index + 1.0) / files.Length));
        }
    }

    private CopyrightAvoidanceOptions BuildEncodingOptions()
    {
        if (UseLightPreset)
        {
            return new CopyrightAvoidanceOptions
            {
                ScaleFactor = 0.98,
                AudioPitchFactor = EncodePitchShift ? 1.02 : null
            };
        }

        if (UseMediumPreset)
        {
            return new CopyrightAvoidanceOptions
            {
                HorizontalFlip = EncodeHorizontalFlip,
                ScaleFactor = 0.96,
                BrightnessAdjust = EncodeBrightness ? 0.03 : null,
                AudioPitchFactor = EncodePitchShift ? 1.04 : null
            };
        }

        // Heavy preset
        return new CopyrightAvoidanceOptions
        {
            HorizontalFlip = EncodeHorizontalFlip,
            ScaleFactor = 0.94,
            RotationDegrees = 1.5,
            BrightnessAdjust = EncodeBrightness ? 0.05 : null,
            ContrastAdjust = 1.05,
            BlurSigma = 0.5,
            AudioPitchFactor = EncodePitchShift ? 1.05 : null,
            SpeedFactor = EncodeSpeedChange ? 1.03 : null
        };
    }

    private VideoQualityPreference GetQualityPreference() => SelectedQuality switch
    {
        "Highest" => VideoQualityPreference.Highest,
        "1080p" => VideoQualityPreference.UpTo1080p,
        "720p" => VideoQualityPreference.UpTo720p,
        "480p" => VideoQualityPreference.UpTo480p,
        "360p" => VideoQualityPreference.UpTo360p,
        "Lowest" => VideoQualityPreference.Lowest,
        _ => VideoQualityPreference.UpTo1080p
    };

    private void AddLog(string message)
    {
        ProcessingLog.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray());
        return sanitized.Length > 100 ? sanitized[..100] : sanitized;
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _uploader?.Dispose();
            _cancellationTokenSource?.Dispose();
        }

        base.Dispose(disposing);
    }
}
