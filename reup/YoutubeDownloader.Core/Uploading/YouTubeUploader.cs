using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Gress;

namespace YoutubeDownloader.Core.Uploading;

/// <summary>
/// Privacy status for YouTube videos
/// </summary>
public enum VideoPrivacyStatus
{
    Public,
    Private,
    Unlisted
}

/// <summary>
/// Category for YouTube videos
/// </summary>
public enum VideoCategory
{
    FilmAnimation = 1,
    AutosVehicles = 2,
    Music = 10,
    PetsAnimals = 15,
    Sports = 17,
    ShortMovies = 18,
    TravelEvents = 19,
    Gaming = 20,
    Videoblogging = 21,
    PeopleBlogs = 22,
    Comedy = 23,
    Entertainment = 24,
    NewsPolitics = 25,
    HowtoStyle = 26,
    Education = 27,
    ScienceTechnology = 28,
    NonprofitsActivism = 29
}

/// <summary>
/// Options for uploading a video to YouTube
/// </summary>
public class VideoUploadOptions
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] Tags { get; set; } = [];
    public VideoPrivacyStatus PrivacyStatus { get; set; } = VideoPrivacyStatus.Private;
    public VideoCategory Category { get; set; } = VideoCategory.Entertainment;
    public string? PlaylistId { get; set; }
    public bool NotifySubscribers { get; set; } = true;
    public bool MadeForKids { get; set; } = false;
    public string? ThumbnailPath { get; set; }
    public DateTime? ScheduledPublishTime { get; set; }
}

/// <summary>
/// Result of a video upload operation
/// </summary>
public class VideoUploadResult
{
    public bool Success { get; set; }
    public string? VideoId { get; set; }
    public string? VideoUrl { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Service for uploading videos to YouTube using Service Account or OAuth
/// </summary>
public class YouTubeUploader : IDisposable
{
    private YouTubeService? _youtubeService;
    private readonly string? _serviceAccountJsonPath;
    private readonly string? _channelId;

    /// <summary>
    /// Initialize with Service Account credentials
    /// </summary>
    public YouTubeUploader(string serviceAccountJsonPath, string? channelId = null)
    {
        _serviceAccountJsonPath = serviceAccountJsonPath;
        _channelId = channelId;
    }

    /// <summary>
    /// Initialize the YouTube service with Service Account
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_youtubeService != null)
            return;

        if (string.IsNullOrEmpty(_serviceAccountJsonPath) || !File.Exists(_serviceAccountJsonPath))
            throw new InvalidOperationException("Service Account JSON file not found");

        var credential = GoogleCredential
            .FromFile(_serviceAccountJsonPath)
            .CreateScoped(YouTubeService.Scope.Youtube, YouTubeService.Scope.YoutubeUpload);

        _youtubeService = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "YoutubeDownloader Reup Tool"
        });

        await Task.CompletedTask;
    }

    /// <summary>
    /// Upload a video to YouTube
    /// </summary>
    public async Task<VideoUploadResult> UploadVideoAsync(
        string videoFilePath,
        VideoUploadOptions options,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_youtubeService == null)
            await InitializeAsync(cancellationToken);

        if (_youtubeService == null)
            return new VideoUploadResult { Success = false, ErrorMessage = "YouTube service not initialized" };

        try
        {
            var video = new Video
            {
                Snippet = new VideoSnippet
                {
                    Title = options.Title.Length > 100 ? options.Title[..100] : options.Title,
                    Description = options.Description.Length > 5000 ? options.Description[..5000] : options.Description,
                    Tags = options.Tags,
                    CategoryId = ((int)options.Category).ToString(),
                    DefaultLanguage = "en",
                    DefaultAudioLanguage = "en"
                },
                Status = new VideoStatus
                {
                    PrivacyStatus = options.PrivacyStatus.ToString().ToLowerInvariant(),
                    MadeForKids = options.MadeForKids,
                    SelfDeclaredMadeForKids = options.MadeForKids
                }
            };

            // Set scheduled publish time if provided
            if (options.ScheduledPublishTime.HasValue && options.PrivacyStatus == VideoPrivacyStatus.Private)
            {
                video.Status.PublishAt = options.ScheduledPublishTime.Value.ToUniversalTime();
            }

            // If channel ID is specified, set it (for Service Account impersonation)
            if (!string.IsNullOrEmpty(_channelId))
            {
                video.Snippet.ChannelId = _channelId;
            }

            await using var fileStream = new FileStream(videoFilePath, FileMode.Open, FileAccess.Read);
            var fileLength = fileStream.Length;

            var videosInsertRequest = _youtubeService.Videos.Insert(
                video,
                "snippet,status",
                fileStream,
                "video/*"
            );

            videosInsertRequest.NotifySubscribers = options.NotifySubscribers;

            videosInsertRequest.ProgressChanged += (uploadProgress) =>
            {
                if (fileLength > 0)
                {
                    var fraction = (double)uploadProgress.BytesSent / fileLength;
                    progress?.Report(Percentage.FromFraction(fraction));
                }
            };

            var uploadedVideo = await videosInsertRequest.UploadAsync(cancellationToken);

            if (uploadedVideo.Status == UploadStatus.Failed)
            {
                return new VideoUploadResult
                {
                    Success = false,
                    ErrorMessage = uploadedVideo.Exception?.Message ?? "Upload failed"
                };
            }

            var videoId = videosInsertRequest.ResponseBody?.Id;

            // Upload thumbnail if provided
            if (!string.IsNullOrEmpty(options.ThumbnailPath) && File.Exists(options.ThumbnailPath) && !string.IsNullOrEmpty(videoId))
            {
                await UploadThumbnailAsync(videoId, options.ThumbnailPath, cancellationToken);
            }

            // Add to playlist if specified
            if (!string.IsNullOrEmpty(options.PlaylistId) && !string.IsNullOrEmpty(videoId))
            {
                await AddVideoToPlaylistAsync(videoId, options.PlaylistId, cancellationToken);
            }

            progress?.Report(Percentage.FromFraction(1.0));

            return new VideoUploadResult
            {
                Success = true,
                VideoId = videoId,
                VideoUrl = $"https://www.youtube.com/watch?v={videoId}"
            };
        }
        catch (Exception ex)
        {
            return new VideoUploadResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Upload a thumbnail for a video
    /// </summary>
    private async Task UploadThumbnailAsync(string videoId, string thumbnailPath, CancellationToken cancellationToken)
    {
        if (_youtubeService == null)
            return;

        await using var thumbnailStream = new FileStream(thumbnailPath, FileMode.Open, FileAccess.Read);
        var mimeType = GetMimeType(thumbnailPath);

        var thumbnailRequest = _youtubeService.Thumbnails.Set(videoId, thumbnailStream, mimeType);
        await thumbnailRequest.UploadAsync(cancellationToken);
    }

    /// <summary>
    /// Add a video to a playlist
    /// </summary>
    private async Task AddVideoToPlaylistAsync(string videoId, string playlistId, CancellationToken cancellationToken)
    {
        if (_youtubeService == null)
            return;

        var playlistItem = new PlaylistItem
        {
            Snippet = new PlaylistItemSnippet
            {
                PlaylistId = playlistId,
                ResourceId = new ResourceId
                {
                    Kind = "youtube#video",
                    VideoId = videoId
                }
            }
        };

        await _youtubeService.PlaylistItems.Insert(playlistItem, "snippet").ExecuteAsync(cancellationToken);
    }

    /// <summary>
    /// Get all playlists for the authenticated channel
    /// </summary>
    public async Task<Playlist[]> GetPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        if (_youtubeService == null)
            await InitializeAsync(cancellationToken);

        if (_youtubeService == null)
            return [];

        var request = _youtubeService.Playlists.List("snippet,contentDetails");
        request.Mine = true;
        request.MaxResults = 50;

        var response = await request.ExecuteAsync(cancellationToken);
        return [.. response.Items];
    }

    /// <summary>
    /// Get channel info for the authenticated account
    /// </summary>
    public async Task<Channel?> GetChannelInfoAsync(CancellationToken cancellationToken = default)
    {
        if (_youtubeService == null)
            await InitializeAsync(cancellationToken);

        if (_youtubeService == null)
            return null;

        var request = _youtubeService.Channels.List("snippet,contentDetails,statistics");
        request.Mine = true;

        var response = await request.ExecuteAsync(cancellationToken);
        return response.Items?.Count > 0 ? response.Items[0] : null;
    }

    /// <summary>
    /// Verify Service Account connection
    /// </summary>
    public async Task<bool> VerifyConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await InitializeAsync(cancellationToken);
            var channel = await GetChannelInfoAsync(cancellationToken);
            return channel != null;
        }
        catch
        {
            return false;
        }
    }

    private static string GetMimeType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "image/jpeg"
        };
    }

    public void Dispose()
    {
        _youtubeService?.Dispose();
    }
}
