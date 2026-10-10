using System.Collections.Generic;

namespace yChicaTube.Services.Remote.Models;

public class TvPlaybackState
{
    public string Status { get; set; } = "idle"; // "idle", "playing", "paused", "buffering", "stopped"
    public TvVideoItem? CurrentVideo { get; set; }
    public double CurrentPositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public int RemainingSessionSeconds { get; set; }
    public bool IsSessionExpired { get; set; }
    public List<TvVideoItem> Queue { get; set; } = new();
    public int QueueIndex { get; set; }
    public string TvName { get; set; } = "yChicaTube TV";
}
