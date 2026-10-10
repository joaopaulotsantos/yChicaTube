using System;
using System.Collections.Generic;

namespace yChicaTube.Services.Remote.Models;

public class TvSyncApprovedVideo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsDiscovered { get; set; }
}

public class TvSyncApprovedPlaylist
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ChannelTitle { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public int VideoCount { get; set; }
    public List<TvSyncApprovedVideo> Videos { get; set; } = new();
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;
}

public class TvSyncBlockedVideo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public DateTime BlockedAt { get; set; } = DateTime.UtcNow;
}

public class TvSyncPayload
{
    public string SourceDeviceId { get; set; } = string.Empty;
    public string SourceDeviceName { get; set; } = string.Empty;
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

    public List<TvSyncApprovedVideo> Videos { get; set; } = new();
    public List<TvSyncApprovedPlaylist> Playlists { get; set; } = new();
    public List<TvSyncBlockedVideo> BlockedVideos { get; set; } = new();
    public List<string> AllowedThemes { get; set; } = new();
    public int MealDurationMinutes { get; set; } = 30;
    public string PrimaryLanguage { get; set; } = "pt";
    public string SecondaryLanguage { get; set; } = "en";
    public string TertiaryLanguage { get; set; } = "es";
    public string? Pin { get; set; }
    public bool AllowChildDiscover { get; set; } = false;
}
