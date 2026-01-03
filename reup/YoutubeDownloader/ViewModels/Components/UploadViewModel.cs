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
using YoutubeDownloader.Core.Uploading;
using YoutubeDownloader.Framework;

namespace YoutubeDownloader.ViewModels.Components;

public partial class UploadQueueItem : ObservableObject
{
    [ObservableProperty]
    public partial string FileName { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "Pending";

    [ObservableProperty]
    public partial string? VideoUrl { get; set; }
}

public partial class UploadViewModel : ViewModelBase
{
    private readonly DialogManager _dialogManager;
    private CancellationTokenSource? _cancellationTokenSource;
    private YouTubeUploader? _uploader;

    public UploadViewModel(DialogManager dialogManager)
    {
        _dialogManager = dialogManager;
        Categories = Enum.GetValues<VideoCategory>().Select(c => c.ToString()).ToArray();
        PrivacyOptions = Enum.GetValues<VideoPrivacyStatus>().Select(p => p.ToString()).ToArray();
        SelectedCategory = "Entertainment";
        SelectedPrivacy = "Private";
    }

    [ObservableProperty]
    public partial string? ServiceAccountPath { get; set; }

    [ObservableProperty]
    public partial string? VideoFilePath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotUploading))]
    [NotifyPropertyChangedFor(nameof(CanUpload))]
    public partial bool IsUploading { get; set; }

    public bool IsNotUploading => !IsUploading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpload))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string ConnectionStatus { get; set; } = "Not connected";

    [ObservableProperty]
    public partial string? ChannelName { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBatchMode { get; set; }

    [ObservableProperty]
    public partial int BatchFileCount { get; set; }

    // Video Metadata
    [ObservableProperty]
    public partial string VideoTitle { get; set; } = "{filename}";

    [ObservableProperty]
    public partial string VideoDescription { get; set; } = "";

    [ObservableProperty]
    public partial string VideoTags { get; set; } = "";

    [ObservableProperty]
    public partial string? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial string? SelectedPrivacy { get; set; }

    [ObservableProperty]
    public partial bool NotifySubscribers { get; set; } = true;

    [ObservableProperty]
    public partial bool MadeForKids { get; set; }

    [ObservableProperty]
    public partial string? ThumbnailPath { get; set; }

    public string[] Categories { get; }
    public string[] PrivacyOptions { get; }

    public bool CanUpload => !IsUploading && IsConnected && !string.IsNullOrWhiteSpace(VideoFilePath);

    public ProgressContainer<Percentage> Progress { get; } = new();

    public ObservableCollection<UploadQueueItem> UploadQueue { get; } = [];

    private string[]? _batchFiles;

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
            await TestConnectionAsync();
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ServiceAccountPath))
        {
            ConnectionStatus = "No service account file selected";
            return;
        }

        StatusMessage = "Testing connection...";

        try
        {
            _uploader?.Dispose();
            _uploader = new YouTubeUploader(ServiceAccountPath);

            var connected = await _uploader.VerifyConnectionAsync();

            if (connected)
            {
                IsConnected = true;
                var channel = await _uploader.GetChannelInfoAsync();
                ChannelName = channel?.Snippet?.Title ?? "Unknown Channel";
                ConnectionStatus = "Connected";
                StatusMessage = $"Successfully connected to: {ChannelName}";
            }
            else
            {
                IsConnected = false;
                ConnectionStatus = "Connection failed";
                StatusMessage = "Could not connect. Make sure the Service Account has access to your YouTube channel.";
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionStatus = "Error";
            StatusMessage = $"Connection error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SelectVideoAsync()
    {
        var filePath = await _dialogManager.PromptOpenFilePathAsync(
            [
                new FilePickerFileType("Video files")
                {
                    Patterns = ["*.mp4", "*.webm", "*.mkv", "*.avi", "*.mov", "*.flv"],
                },
            ]
        );

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            VideoFilePath = filePath;
            IsBatchMode = false;
            _batchFiles = null;
            BatchFileCount = 0;
            OnPropertyChanged(nameof(CanUpload));
        }
    }

    [RelayCommand]
    private async Task SelectVideoFolderAsync()
    {
        var folderPath = await _dialogManager.PromptDirectoryPathAsync();

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            VideoFilePath = folderPath;
            IsBatchMode = true;
            _batchFiles = Directory.GetFiles(folderPath, "*.*")
                .Where(f => IsVideoFile(f))
                .ToArray();
            BatchFileCount = _batchFiles.Length;
            OnPropertyChanged(nameof(CanUpload));
        }
    }

    [RelayCommand]
    private async Task SelectThumbnailAsync()
    {
        var filePath = await _dialogManager.PromptOpenFilePathAsync(
            [
                new FilePickerFileType("Image files")
                {
                    Patterns = ["*.jpg", "*.jpeg", "*.png", "*.gif"],
                },
            ]
        );

        if (!string.IsNullOrWhiteSpace(filePath))
            ThumbnailPath = filePath;
    }

    [RelayCommand]
    private void ClearThumbnail()
    {
        ThumbnailPath = null;
    }

    [RelayCommand]
    private async Task StartUploadAsync()
    {
        if (_uploader == null || string.IsNullOrWhiteSpace(VideoFilePath))
            return;

        IsUploading = true;
        _cancellationTokenSource = new CancellationTokenSource();
        UploadQueue.Clear();

        try
        {
            if (IsBatchMode && _batchFiles != null)
            {
                var totalFiles = _batchFiles.Length;
                foreach (var (index, file) in _batchFiles.Select((f, i) => (i, f)))
                {
                    _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                    var queueItem = new UploadQueueItem
                    {
                        FileName = Path.GetFileName(file),
                        Status = "Uploading..."
                    };
                    UploadQueue.Add(queueItem);

                    StatusMessage = $"Uploading {index + 1}/{totalFiles}: {queueItem.FileName}";

                    var result = await UploadSingleVideoAsync(file, index);

                    if (result.Success)
                    {
                        queueItem.Status = "Completed";
                        queueItem.VideoUrl = result.VideoUrl;
                    }
                    else
                    {
                        queueItem.Status = $"Failed: {result.ErrorMessage}";
                    }

                    Progress.Report(Percentage.FromFraction((index + 1.0) / totalFiles));
                }

                StatusMessage = $"Batch upload completed! {totalFiles} videos processed.";
            }
            else
            {
                var queueItem = new UploadQueueItem
                {
                    FileName = Path.GetFileName(VideoFilePath),
                    Status = "Uploading..."
                };
                UploadQueue.Add(queueItem);

                StatusMessage = $"Uploading: {queueItem.FileName}";

                var result = await UploadSingleVideoAsync(VideoFilePath, 0);

                if (result.Success)
                {
                    queueItem.Status = "Completed";
                    queueItem.VideoUrl = result.VideoUrl;
                    StatusMessage = $"Upload completed! Video URL: {result.VideoUrl}";
                }
                else
                {
                    queueItem.Status = $"Failed: {result.ErrorMessage}";
                    StatusMessage = $"Upload failed: {result.ErrorMessage}";
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Upload cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsUploading = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private async Task<VideoUploadResult> UploadSingleVideoAsync(string filePath, int index)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var title = VideoTitle.Replace("{filename}", fileName).Replace("{index}", (index + 1).ToString());

        var options = new VideoUploadOptions
        {
            Title = title,
            Description = VideoDescription,
            Tags = VideoTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Category = Enum.TryParse<VideoCategory>(SelectedCategory, out var cat) ? cat : VideoCategory.Entertainment,
            PrivacyStatus = Enum.TryParse<VideoPrivacyStatus>(SelectedPrivacy, out var priv) ? priv : VideoPrivacyStatus.Private,
            NotifySubscribers = NotifySubscribers,
            MadeForKids = MadeForKids,
            ThumbnailPath = ThumbnailPath
        };

        return await _uploader!.UploadVideoAsync(
            filePath,
            options,
            Progress,
            _cancellationTokenSource!.Token
        );
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
    }

    private static bool IsVideoFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp4" or ".webm" or ".mkv" or ".avi" or ".mov" or ".flv";
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
