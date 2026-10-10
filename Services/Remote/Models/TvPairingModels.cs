namespace yChicaTube.Services.Remote.Models;

public class TvPairRequest
{
    public string ClientDeviceId { get; set; } = string.Empty;
    public string ClientDeviceName { get; set; } = string.Empty;
    public string PairingCode { get; set; } = string.Empty;
}

public class TvPairResponse
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string TvId { get; set; } = string.Empty;
    public string TvName { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
}

public class TvDiscoveryPacket
{
    public string Type { get; set; } = "YCHICATUBE_TV_ANNOUNCE";
    public string TvId { get; set; } = string.Empty;
    public string TvName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 8942;
    public bool IsPaired { get; set; }
}

public class PairedClientInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public DateTime PairedAt { get; set; } = DateTime.UtcNow;
}
