using System.Collections.Generic;

namespace yChicaTube.Services.Remote.Models;

public class TvRemoteCommand
{
    // Actions: "play", "pause", "resume", "stop", "next", "prev", "seek", 
    //          "play_video", "add_to_queue", "replace_queue", "remove_from_queue", "clear_queue", "add_time"
    public string Action { get; set; } = string.Empty;
    public TvVideoItem? Video { get; set; }
    public List<TvVideoItem>? Videos { get; set; }
    public double PositionSeconds { get; set; }
    public string? VideoId { get; set; }
    public int MinutesToAdd { get; set; }
    public List<string>? UnlockSequence { get; set; }
}

public class TvCommandResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public TvPlaybackState? State { get; set; }
}
