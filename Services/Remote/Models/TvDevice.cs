namespace yChicaTube.Services.Remote.Models;

public class TvDevice
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "TV";
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 8942;
    public string AuthToken { get; set; } = string.Empty;
    public DateTime PairedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public bool IsOnline { get; set; }
    public string StatusText => IsOnline ? "Online" : "Offline";
    public string StatusColor => IsOnline ? "#10B981" : "#94A3B8";
    public string BaseUrl => $"http://{IpAddress}:{Port}";
}
