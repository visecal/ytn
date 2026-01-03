using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gress;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Framework;

namespace YoutubeDownloader.ViewModels.Components;

public partial class EncodeViewModel : ViewModelBase
{
    private readonly DialogManager _dialogManager;
    private CancellationTokenSource? _cancellationTokenSource;

    public EncodeViewModel(DialogManager dialogManager)
    {
        _dialogManager = dialogManager;
    }

    [ObservableProperty]
    public partial string? InputVideoPath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotProcessing))]
    public partial bool IsProcessing { get; set; }

    public bool IsNotProcessing => !IsProcessing;

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial string? OutputPath { get; set; }

    [ObservableProperty]
    public partial string OutputSuffix { get; set; } = "_encoded";

    [ObservableProperty]
    public partial bool IsBatchMode { get; set; }

    [ObservableProperty]
    public partial int BatchFileCount { get; set; }

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

    private string[]? _batchFiles;

    [RelayCommand]
    private async Task SelectInputVideoAsync()
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
            InputVideoPath = filePath;
            IsBatchMode = false;
            _batchFiles = null;
            BatchFileCount = 0;
        }
    }

    [RelayCommand]
    private async Task SelectInputFolderAsync()
    {
        var folderPath = await _dialogManager.PromptDirectoryPathAsync();

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            InputVideoPath = folderPath;
            IsBatchMode = true;
            _batchFiles = Directory.GetFiles(folderPath, "*.*")
                .Where(f => IsVideoFile(f))
                .ToArray();
            BatchFileCount = _batchFiles.Length;
        }
    }

    [RelayCommand]
    private async Task SelectOutputFolderAsync()
    {
        var folderPath = await _dialogManager.PromptDirectoryPathAsync();

        if (!string.IsNullOrWhiteSpace(folderPath))
            OutputPath = folderPath;
    }

    [RelayCommand]
    private void ApplyLightPreset()
    {
        ResetOptions();
        ApplyScaling = true;
        ScaleFactor = 0.98;
        ApplyPitchShift = true;
        PitchFactor = 1.02;
    }

    [RelayCommand]
    private void ApplyMediumPreset()
    {
        ResetOptions();
        HorizontalFlip = true;
        ApplyScaling = true;
        ScaleFactor = 0.96;
        ApplyBrightness = true;
        BrightnessAdjust = 0.03;
        ApplyPitchShift = true;
        PitchFactor = 1.04;
    }

    [RelayCommand]
    private void ApplyHeavyPreset()
    {
        HorizontalFlip = true;
        ApplyScaling = true;
        ScaleFactor = 0.94;
        ApplyRotation = true;
        RotationDegrees = 1.5;
        ApplyBrightness = true;
        BrightnessAdjust = 0.05;
        ApplyContrast = true;
        ContrastAdjust = 1.05;
        ApplyBlur = true;
        BlurSigma = 0.5;
        ApplyPitchShift = true;
        PitchFactor = 1.05;
        ApplySpeedChange = true;
        SpeedFactor = 1.03;
    }

    [RelayCommand]
    private void ResetOptions()
    {
        HorizontalFlip = false;
        VerticalFlip = false;
        ApplyScaling = false;
        ScaleFactor = 0.98;
        ApplyRotation = false;
        RotationDegrees = 1.0;
        ApplyBrightness = false;
        BrightnessAdjust = 0.02;
        ApplyContrast = false;
        ContrastAdjust = 1.02;
        ApplyBlur = false;
        BlurSigma = 0.3;
        ApplyPitchShift = false;
        PitchFactor = 1.03;
        ApplySpeedChange = false;
        SpeedFactor = 1.02;
    }

    [RelayCommand]
    private async Task StartEncodingAsync()
    {
        if (string.IsNullOrWhiteSpace(InputVideoPath))
        {
            StatusMessage = "Please select input video or folder";
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            OutputPath = IsBatchMode ? InputVideoPath : Path.GetDirectoryName(InputVideoPath);
        }

        IsProcessing = true;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var processor = new VideoProcessor();
            var options = BuildOptions();

            if (IsBatchMode && _batchFiles != null)
            {
                var totalFiles = _batchFiles.Length;
                for (var i = 0; i < totalFiles; i++)
                {
                    _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                    var inputFile = _batchFiles[i];
                    var fileName = Path.GetFileNameWithoutExtension(inputFile);
                    var extension = Path.GetExtension(inputFile);
                    var outputFile = Path.Combine(OutputPath!, $"{fileName}{OutputSuffix}{extension}");

                    StatusMessage = $"Processing {i + 1}/{totalFiles}: {Path.GetFileName(inputFile)}";

                    await processor.ProcessForCopyrightAvoidanceAsync(
                        inputFile,
                        outputFile,
                        options,
                        Progress,
                        _cancellationTokenSource.Token
                    );

                    Progress.Report(Percentage.FromFraction((i + 1.0) / totalFiles));
                }

                StatusMessage = $"Batch encoding completed! {totalFiles} files processed.";
            }
            else
            {
                var fileName = Path.GetFileNameWithoutExtension(InputVideoPath);
                var extension = Path.GetExtension(InputVideoPath);
                var outputFile = Path.Combine(OutputPath!, $"{fileName}{OutputSuffix}{extension}");

                StatusMessage = $"Processing: {Path.GetFileName(InputVideoPath)}";

                await processor.ProcessForCopyrightAvoidanceAsync(
                    InputVideoPath,
                    outputFile,
                    options,
                    Progress,
                    _cancellationTokenSource.Token
                );

                StatusMessage = "Encoding completed successfully!";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Encoding cancelled";
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

    private CopyrightAvoidanceOptions BuildOptions() => new()
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

    private static bool IsVideoFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp4" or ".webm" or ".mkv" or ".avi" or ".mov" or ".flv";
    }
}
