using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using yChicaTube.Services.Remote.Models;

namespace yChicaTube.Services.Remote;

public class TvClientService
{
    private static readonly Lazy<TvClientService> _instance = new(() => new TvClientService());
    public static TvClientService Instance => _instance.Value;

    private readonly HttpClient _httpClient;

    public TvClientService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    // ==========================================
    // 1. DESCOBERTA E PING
    // ==========================================

    public async Task<List<TvDiscoveryPacket>> DiscoverTvsAsync(int timeoutMs = 2500)
    {
        return await TvDiscoveryService.Instance.DiscoverTvsAsync(timeoutMs);
    }

    public async Task<bool> CheckTvOnlineAsync(TvDevice tv)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var response = await _httpClient.GetAsync($"{tv.BaseUrl}/api/v1/ping", cts.Token);
            var isOnline = response.IsSuccessStatusCode;
            tv.IsOnline = isOnline;
            if (isOnline) tv.LastSeenAt = DateTime.UtcNow;
            return isOnline;
        }
        catch
        {
            tv.IsOnline = false;
            return false;
        }
    }

    public async Task<bool> CheckTvAuthPendingAsync(TvDevice tv)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var response = await _httpClient.GetAsync($"{tv.BaseUrl}/api/v1/auth_status", cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var resBody = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(resBody);
                if (doc.RootElement.TryGetProperty("isAuthPending", out var prop) && prop.GetBoolean())
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    // ==========================================
    // 2. EMPARELHAMENTO
    // ==========================================

    public async Task<(bool success, string message, TvDevice? tv)> PairWithTvAsync(string ipAddress, int port, string pairingCode, string? customName = null)
    {
        try
        {
            var cleanIp = ipAddress.Trim();
            var baseUrl = $"http://{cleanIp}:{port}";

            var requestPayload = new TvPairRequest
            {
                ClientDeviceId = TvPairingManager.Instance.ClientDeviceId,
                ClientDeviceName = TvPairingManager.Instance.ClientDeviceName,
                PairingCode = pairingCode.Replace(" ", "").Trim()
            };

            var json = JsonSerializer.Serialize(requestPayload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var response = await _httpClient.PostAsync($"{baseUrl}/api/v1/pair", content, cts.Token);

            var resBody = await response.Content.ReadAsStringAsync();
            var pairResponse = JsonSerializer.Deserialize<TvPairResponse>(resBody);

            if (response.IsSuccessStatusCode && pairResponse != null && pairResponse.Success)
            {
                var newTv = new TvDevice
                {
                    Id = pairResponse.TvId,
                    Name = !string.IsNullOrWhiteSpace(customName) ? customName : (!string.IsNullOrWhiteSpace(pairResponse.TvName) ? pairResponse.TvName : "TV da Sala"),
                    IpAddress = cleanIp,
                    Port = port,
                    AuthToken = pairResponse.AuthToken,
                    IsOnline = true,
                    LastSeenAt = DateTime.UtcNow,
                    PairedAt = DateTime.UtcNow
                };

                TvPairingManager.Instance.AddOrUpdatePairedTv(newTv);
                return (true, "TV emparelhada com sucesso!", newTv);
            }

            var errMsg = pairResponse?.ErrorMessage ?? "Falha no emparelhamento. Verifique o código e tente novamente.";
            return (false, errMsg, null);
        }
        catch (Exception ex)
        {
            return (false, $"Erro de ligação: {ex.Message}", null);
        }
    }

    // ==========================================
    // 3. ENVIO DE VÍDEOS & REPRODUÇÃO
    // ==========================================

    public async Task<(bool success, string message)> PlayNowAsync(TvDevice tv, TvVideoItem video)
    {
        try
        {
            var json = JsonSerializer.Serialize(video);
            using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"{tv.BaseUrl}/api/v1/playback/play-now", tv.AuthToken, json);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await _httpClient.SendAsync(request, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                tv.IsOnline = true;
                tv.LastSeenAt = DateTime.UtcNow;
                return (true, "Vídeo enviado para a TV!");
            }

            return (false, $"A TV respondeu com erro ({response.StatusCode}).");
        }
        catch (Exception ex)
        {
            return (false, $"Erro ao comunicar com a TV: {ex.Message}");
        }
    }

    public async Task<(bool success, string message)> AddVideosToQueueAsync(TvDevice tv, List<TvVideoItem> videos)
    {
        try
        {
            var json = JsonSerializer.Serialize(videos);
            using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"{tv.BaseUrl}/api/v1/queue/add", tv.AuthToken, json);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var response = await _httpClient.SendAsync(request, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                tv.IsOnline = true;
                tv.LastSeenAt = DateTime.UtcNow;
                return (true, $"{videos.Count} histórias adicionadas à fila da TV!");
            }

            return (false, $"A TV rejeitou os vídeos ({response.StatusCode}).");
        }
        catch (Exception ex)
        {
            return (false, $"Erro de envio: {ex.Message}");
        }
    }

    public async Task<(bool success, string message)> ReplaceQueueAndPlayAsync(TvDevice tv, List<TvVideoItem> videos)
    {
        try
        {
            var json = JsonSerializer.Serialize(videos);
            using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"{tv.BaseUrl}/api/v1/queue/replace", tv.AuthToken, json);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var response = await _httpClient.SendAsync(request, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                tv.IsOnline = true;
                tv.LastSeenAt = DateTime.UtcNow;
                return (true, "Seleção enviada para a TV!");
            }

            return (false, $"A TV rejeitou o pedido ({response.StatusCode}).");
        }
        catch (Exception ex)
        {
            return (false, $"Erro de envio: {ex.Message}");
        }
    }

    // ==========================================
    // 4. COMANDOS REMOTOS & ESTADO
    // ==========================================

    public async Task<(bool success, string message)> SendCommandAsync(TvDevice tv, TvRemoteCommand command)
    {
        try
        {
            var json = JsonSerializer.Serialize(command);
            using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"{tv.BaseUrl}/api/v1/playback/command", tv.AuthToken, json);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var response = await _httpClient.SendAsync(request, cts.Token);

            return (response.IsSuccessStatusCode, response.IsSuccessStatusCode ? "Comando enviado." : "Erro ao enviar comando.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool success, string message)> UnlockParentAreaOnTvAsync(TvDevice tv)
    {
        return await SendCommandAsync(tv, new TvRemoteCommand { Action = "unlock_parent_area" });
    }

    public async Task<(bool success, string message)> LockParentAreaOnTvAsync(TvDevice tv)
    {
        return await SendCommandAsync(tv, new TvRemoteCommand { Action = "lock_parent_area" });
    }

    public async Task<(bool success, string message)> SyncDataToTvAsync(TvDevice tv, TvSyncPayload payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"{tv.BaseUrl}/api/v1/sync_data", tv.AuthToken, json);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await _httpClient.SendAsync(request, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                tv.IsOnline = true;
                tv.LastSeenAt = DateTime.UtcNow;
                return (true, "Definições e biblioteca sincronizadas com a TV!");
            }

            return (false, $"A TV respondeu com erro ({response.StatusCode}).");
        }
        catch (Exception ex)
        {
            return (false, $"Erro ao sincronizar com a TV: {ex.Message}");
        }
    }

    public async Task<TvPlaybackState?> GetPlaybackStateAsync(TvDevice tv)
    {
        try
        {
            using var request = CreateAuthenticatedRequest(HttpMethod.Get, $"{tv.BaseUrl}/api/v1/state", tv.AuthToken);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var response = await _httpClient.SendAsync(request, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<TvPlaybackState>(body);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url, string authToken, string? jsonBody = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        request.Headers.Add("X-Device-Id", TvPairingManager.Instance.ClientDeviceId);

        if (!string.IsNullOrEmpty(jsonBody))
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return request;
    }
}
