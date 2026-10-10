namespace yChicaTube.Services.Remote.Models;

public class TvVideoItem
{
    public string VideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string ChannelTitle { get; set; } = string.Empty;
    public string DurationText { get; set; } = string.Empty;
    public int Position { get; set; }

    public static TvVideoItem FromApprovedVideo(ApprovedVideo video, int position = 0)
    {
        return new TvVideoItem
        {
            VideoId = video.Id,
            Title = video.Title,
            ThumbnailUrl = video.ThumbnailUrl,
            Position = position
        };
    }

    public static TvVideoItem FromParentVideoItem(ParentVideoItem video, int position = 0)
    {
        return new TvVideoItem
        {
            VideoId = video.Id,
            Title = video.Title,
            ThumbnailUrl = video.ThumbnailUrl,
            Position = position
        };
    }

    public ApprovedVideo ToApprovedVideo()
    {
        return new ApprovedVideo(VideoId, Title, false);
    }
}
