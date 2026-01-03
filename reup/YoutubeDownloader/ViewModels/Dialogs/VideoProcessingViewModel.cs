using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gress;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Framework;

namespace YoutubeDownloader.ViewModels.Dialogs;

public partial class VideoProcessingViewModel(DialogManager dialogManager)
    : DialogViewModelBase<bool>
{
    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    public partial string? VideoFilePath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotProcessing))]
    public partial bool IsProcessing { get; set; }

    public bool IsNotProcessing => !IsProcessing;

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public ProgressContainer<Percentage> Progress { get; } = new();

    // Copyright Avoidance Options
    [ObservableProperty]
    public partial bool HorizontalFlip { get; set; }

    [ObservableProperty]
    public partial bool VerticalFlip { get; set; }

    [ObservableProperty]
    public partial bool ApplyScaling { get; set; }

    [ObservableProperty]
    public partial double ScaleFactor { get; set; } = 0.98;

    [ObservableProperty]
    public partial bool ApplyPitchShift { get; set; }

    [ObservableProperty]
    public partial double PitchFactor { get; set; } = 1.03;

    [ObservableProperty]
    public partial bool ApplySpeedChange { get; set; }

    [ObservableProperty]
    public partial double SpeedFactor { get; set; } = 1.02;

    [ObservableProperty]
    public partial bool ApplyRotation { get; set; }

    [ObservableProperty]
    public partial double RotationDegrees { get; set; } = 1.0;

    [ObservableProperty]
    public partial bool ApplyBrightness { get; set; }

    [ObservableProperty]
    public partial double BrightnessAdjust { get; set; } = 0.02;

    [ObservableProperty]
    public partial bool ApplyContrast { get; set; }

    [ObservableProperty]
    public partial double ContrastAdjust { get; set; } = 1.02;

    [ObservableProperty]
    public partial bool ApplyBlur { get; set; }

    [ObservableProperty]
    public partial double BlurSigma { get; set; } = 0.3;

    // Voice Merge Options
    [ObservableProperty]
    public partial string? VoiceFilePath { get; set; }

    [ObservableProperty]
    public partial bool MuteOriginalAudio { get; set; }

    [ObservableProperty]
    public partial double VoiceVolume { get; set; } = 1.0;

    [ObservableProperty]
    public partial double OriginalAudioVolume { get; set; } = 0.3;

    [RelayCommand]
    private async Task SelectVideoFileAsync()
    {
        var filePath = await dialogManager.PromptOpenFilePathAsync(
            [
                new FilePickerFileType("Video files")
                {
                    Patterns = ["*.mp4", "*.webm", "*.mkv", "*.avi", "*.mov", "*.flv"],
                },
            ]
        );

        if (!string.IsNullOrWhiteSpace(filePath))
            VideoFilePath = filePath;
    }

    [RelayCommand]
    private async Task SelectVoiceFileAsync()
    {
        var filePath = await dialogManager.PromptOpenFilePathAsync(
            [
                new FilePickerFileType("Audio files")
                {
                    Patterns = ["*.mp3", "*.wav", "*.aac", "*.ogg", "*.m4a", "*.flac"],
                },
            ]
        );

        if (!string.IsNullOrWhiteSpace(filePath))
            VoiceFilePath = filePath;
    }

    [RelayCommand]
    private async Task ExtractAudioAsync()
    {
        if (string.IsNullOrWhiteSpace(VideoFilePath))
            return;

        var outputPath = await dialogManager.PromptSaveFilePathAsync(
            [new FilePickerFileType("MP3 file") { Patterns = ["*.mp3"] }],
            Path.GetFileNameWithoutExtension(VideoFilePath) + "_audio.mp3"
        );

        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        IsProcessing = true;
        StatusMessage = "Extracting audio...";
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var processor = new VideoProcessor();
            await processor.ExtractAudioAsync(
                VideoFilePath,
                outputPath,
                Progress,
                _cancellationTokenSource.Token
            );
            StatusMessage = "Audio extracted successfully!";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private async Task ProcessCopyrightAvoidanceAsync()
    {
        if (string.IsNullOrWhiteSpace(VideoFilePath))
            return;

        var extension = Path.GetExtension(VideoFilePath);
        var outputPath = await dialogManager.PromptSaveFilePathAsync(
            [
                new FilePickerFileType($"{extension.TrimStart('.')} file")
                {
                    Patterns = [$"*{extension}"],
                },
            ],
            Path.GetFileNameWithoutExtension(VideoFilePath) + "_processed" + extension
        );

        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        IsProcessing = true;
        StatusMessage = "Processing video for copyright avoidance...";
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var processor = new VideoProcessor();
            var options = new CopyrightAvoidanceOptions
            {
                HorizontalFlip = HorizontalFlip,
                VerticalFlip = VerticalFlip,
                ScaleFactor = ApplyScaling ? ScaleFactor : null,
                AudioPitchFactor = ApplyPitchShift ? PitchFactor : null,
                SpeedFactor = ApplySpeedChange ? SpeedFactor : null,
                RotationDegrees = ApplyRotation ? RotationDegrees : null,
                BrightnessAdjust = ApplyBrightness ? BrightnessAdjust : null,
                ContrastAdjust = ApplyContrast ? ContrastAdjust : null,
                BlurSigma = ApplyBlur ? BlurSigma : null,
            };

            await processor.ProcessForCopyrightAvoidanceAsync(
                VideoFilePath,
                outputPath,
                options,
                Progress,
                _cancellationTokenSource.Token
            );
            StatusMessage = "Video processed successfully!";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private async Task MergeVoiceAsync()
    {
        if (string.IsNullOrWhiteSpace(VideoFilePath) || string.IsNullOrWhiteSpace(VoiceFilePath))
            return;

        var extension = Path.GetExtension(VideoFilePath);
        var outputPath = await dialogManager.PromptSaveFilePathAsync(
            [
                new FilePickerFileType($"{extension.TrimStart('.')} file")
                {
                    Patterns = [$"*{extension}"],
                },
            ],
            Path.GetFileNameWithoutExtension(VideoFilePath) + "_merged" + extension
        );

        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        IsProcessing = true;
        StatusMessage = "Merging voice into video...";
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var processor = new VideoProcessor();
            var options = new VoiceMergeOptions
            {
                VoiceFilePath = VoiceFilePath,
                MuteOriginalAudio = MuteOriginalAudio,
                VoiceVolume = VoiceVolume,
                OriginalAudioVolume = OriginalAudioVolume,
            };

            await processor.MergeVoiceIntoVideoAsync(
                VideoFilePath,
                outputPath,
                options,
                Progress,
                _cancellationTokenSource.Token
            );
            StatusMessage = "Voice merged successfully!";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private async Task BatchMergeVoiceAsync()
    {
        var videoDirectory = await dialogManager.PromptDirectoryPathAsync();
        if (string.IsNullOrWhiteSpace(videoDirectory))
            return;

        var voiceDirectory = await dialogManager.PromptDirectoryPathAsync();
        if (string.IsNullOrWhiteSpace(voiceDirectory))
            return;

        var outputDirectory = await dialogManager.PromptDirectoryPathAsync();
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;

        IsProcessing = true;
        StatusMessage = "Batch merging voices into videos...";
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var processor = new VideoProcessor();
            await processor.BatchMergeVoiceAsync(
                videoDirectory,
                voiceDirectory,
                outputDirectory,
                MuteOriginalAudio,
                VoiceVolume,
                OriginalAudioVolume,
                Progress,
                _cancellationTokenSource.Token
            );
            StatusMessage = "Batch merge completed successfully!";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
    }

    [RelayCommand]
    private void CloseDialog()
    {
        Close(true);
    }
}
