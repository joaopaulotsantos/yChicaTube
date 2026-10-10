using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using yChicaTube.Services.Remote.Models;

namespace yChicaTube.Services.Remote;

public class TvServerService
{
    private static readonly Lazy<TvServerService> _instance = new(() => new TvServerService());
    public static TvServerService Instance => _instance.Value;

    public const int ServerPort = 8942;
    private HttpListener? _httpListener;
    private CancellationTokenSource? _serverCts;
    private bool _isRunning;

    // Eventos disparados para a UI da TV
    public event Action<TvVideoItem>? PlayVideoRequested;
    public event Action<TvRemoteCommand>? CommandReceived;
    public event Action? QueueUpdated;
    public event Action<PairedClientInfo>? PairingCompleted;
    public event Action<TvSyncPayload>? SyncDataReceived;
    public Func<TvPlaybackState>? GetCurrentPlaybackState;

    public bool IsRunning => _isRunning;

    // Estado de pedido de autorização remota (Biometria / PIN no telemóvel)
    public bool IsAuthPending { get; set; }
    public DateTime AuthPendingUntil { get; set; } = DateTime.MinValue;

    public void RequestAuthFromPhone(TimeSpan? timeout = null)
    {
        IsAuthPending = true;
        AuthPendingUntil = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromMinutes(2));
    }

    public void ClearAuthPending()
    {
        IsAuthPending = false;
        AuthPendingUntil = DateTime.MinValue;
    }

    public void Start()
    {
        if (_isRunning) return;

        _serverCts = new CancellationTokenSource();
        _isRunning = true;

        Task.Run(async () =>
        {
            try
            {
                _httpListener = new HttpListener();
                
                // Tenta escutar em todas as interfaces
                try
                {
                    _httpListener.Prefixes.Add($"http://*:{ServerPort}/");
                    _httpListener.Start();
                }
                catch
                {
                    _httpListener.Close();
                    _httpListener = new HttpListener();
                    _httpListener.Prefixes.Add($"http://+:{ServerPort}/");
                    _httpListener.Start();
                }

                TvDiscoveryService.Instance.StartAdvertising();

                while (!_serverCts.Token.IsCancellationRequested && _httpListener.IsListening)
                {
                    try
                    {
                        var context = await _httpListener.GetContextAsync();
                        _ = ProcessRequestAsync(context);
                    }
                    catch (HttpListenerException) when (_serverCts.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[TvServer.ListenerLoop] {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TvServer.Start] {ex.Message}");
                _isRunning = false;
            }
        });
    }

    public void Stop()
    {
        _isRunning = false;
        try
        {
            TvDiscoveryService.Instance.StopAdvertising();
            _serverCts?.Cancel();
            _httpListener?.Stop();
            _httpListener?.Close();
            _httpListener = null;
        }
        catch { }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        var req = context.Request;
        var res = context.Response;

        // Permite CORS para facilitar depuração local
        res.Headers.Add("Access-Control-Allow-Origin", "*");
        res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        res.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Device-Id");

        if (req.HttpMethod == "OPTIONS")
        {
            res.StatusCode = 200;
            res.Close();
            return;
        }

        try
        {
            var path = req.Url?.AbsolutePath.ToLowerInvariant() ?? "";
            var method = req.HttpMethod.ToUpperInvariant();

            // 1. PING / STATUS (Acessível sem autenticação)
            if (path == "/api/v1/ping" && method == "GET")
            {
                var pingInfo = new
                {
                    status = "ok",
                    tvId = TvPairingManager.Instance.TvId,
                    tvName = TvPairingManager.Instance.TvName,
                    version = "1.0",
                    isPaired = TvPairingManager.Instance.GetPairedClients().Count > 0,
                    isAuthPending = IsAuthPending && DateTime.UtcNow < AuthPendingUntil
                };
                await WriteJsonResponseAsync(res, 200, pingInfo);
                return;
            }

            // 1B. ESTADO DE AUTORIZAÇÃO DOS PAIS (GET /api/v1/auth_status)
            if (path == "/api/v1/auth_status" && method == "GET")
            {
                var pending = IsAuthPending && DateTime.UtcNow < AuthPendingUntil;
                var authInfo = new
                {
                    isAuthPending = pending,
                    tvId = TvPairingManager.Instance.TvId,
                    tvName = TvPairingManager.Instance.TvName
                };
                await WriteJsonResponseAsync(res, 200, authInfo);
                return;
            }

            // 2. EMPARELHAMENTO (POST /api/v1/pair)
            if (path == "/api/v1/pair" && method == "POST")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var pairRequest = JsonSerializer.Deserialize<TvPairRequest>(body);

                if (pairRequest == null || string.IsNullOrWhiteSpace(pairRequest.PairingCode))
                {
                    await WriteJsonResponseAsync(res, 400, new TvPairResponse
                    {
                        Success = false,
                        ErrorMessage = "Dados de emparelhamento inválidos."
                    });
                    return;
                }

                if (TvPairingManager.Instance.ValidateAndPairClient(pairRequest, out var authToken, out var errorMsg))
                {
                    var response = new TvPairResponse
                    {
                        Success = true,
                        TvId = TvPairingManager.Instance.TvId,
                        TvName = TvPairingManager.Instance.TvName,
                        AuthToken = authToken
                    };

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        PairingCompleted?.Invoke(new PairedClientInfo
                        {
                            DeviceId = pairRequest.ClientDeviceId,
                            DeviceName = pairRequest.ClientDeviceName,
                            AuthToken = authToken
                        });
                    });

                    await WriteJsonResponseAsync(res, 200, response);
                }
                else
                {
                    await WriteJsonResponseAsync(res, 401, new TvPairResponse
                    {
                        Success = false,
                        ErrorMessage = errorMsg
                    });
                }
                return;
            }

            // ==========================================
            // ROTAS AUTENTICADAS (Requerem Bearer Token)
            // ==========================================
            var authHeader = req.Headers["Authorization"];
            if (!TvPairingManager.Instance.IsTokenAuthorized(authHeader))
            {
                await WriteJsonResponseAsync(res, 401, new { error = "Unauthorized. Token inválido ou dispositivo não emparelhado." });
                return;
            }

            // 3. ESTADO DA REPRODUÇÃO (GET /api/v1/state)
            if (path == "/api/v1/state" && method == "GET")
            {
                var state = GetCurrentPlaybackState?.Invoke() ?? new TvPlaybackState
                {
                    TvName = TvPairingManager.Instance.TvName,
                    Queue = TvQueueManager.Instance.Queue.ToList(),
                    QueueIndex = TvQueueManager.Instance.CurrentIndex,
                    CurrentVideo = TvQueueManager.Instance.CurrentVideo
                };
                await WriteJsonResponseAsync(res, 200, state);
                return;
            }

            // 4. REPRODUZIR AGORA (POST /api/v1/playback/play-now)
            if (path == "/api/v1/playback/play-now" && method == "POST")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var video = JsonSerializer.Deserialize<TvVideoItem>(body);

                if (video != null && !string.IsNullOrWhiteSpace(video.VideoId))
                {
                    TvQueueManager.Instance.PlayVideoImmediately(video);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        PlayVideoRequested?.Invoke(video);
                    });

                    await WriteJsonResponseAsync(res, 200, new TvCommandResult { Success = true, Message = "Vídeo iniciado com sucesso." });
                }
                else
                {
                    await WriteJsonResponseAsync(res, 400, new TvCommandResult { Success = false, Message = "Vídeo inválido." });
                }
                return;
            }

            // 5. GESTÃO DA FILA (POST /api/v1/queue/add, replace, remove, clear)
            if (path.StartsWith("/api/v1/queue/") && method == "POST")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync();

                if (path == "/api/v1/queue/add")
                {
                    var videos = JsonSerializer.Deserialize<List<TvVideoItem>>(body);
                    if (videos != null && videos.Count > 0)
                    {
                        TvQueueManager.Instance.EnqueueRange(videos);
                        MainThread.BeginInvokeOnMainThread(() => QueueUpdated?.Invoke());
                        await WriteJsonResponseAsync(res, 200, new TvCommandResult { Success = true, Message = $"{videos.Count} vídeos adicionados à fila." });
                    }
                    else
                    {
                        await WriteJsonResponseAsync(res, 400, new TvCommandResult { Success = false, Message = "Lista de vídeos vazia." });
                    }
                    return;
                }

                if (path == "/api/v1/queue/replace")
                {
                    var videos = JsonSerializer.Deserialize<List<TvVideoItem>>(body);
                    if (videos != null && videos.Count > 0)
                    {
                        TvQueueManager.Instance.SetQueue(videos, 0);
                        var firstVideo = TvQueueManager.Instance.CurrentVideo;
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            QueueUpdated?.Invoke();
                            if (firstVideo != null) PlayVideoRequested?.Invoke(firstVideo);
                        });
                        await WriteJsonResponseAsync(res, 200, new TvCommandResult { Success = true, Message = "Fila substituída com sucesso." });
                    }
                    else
                    {
                        await WriteJsonResponseAsync(res, 400, new TvCommandResult { Success = false, Message = "Lista de vídeos vazia." });
                    }
                    return;
                }

                if (path == "/api/v1/queue/remove")
                {
                    var cmd = JsonSerializer.Deserialize<TvRemoteCommand>(body);
                    if (cmd != null && !string.IsNullOrWhiteSpace(cmd.VideoId))
                    {
                        var removed = TvQueueManager.Instance.RemoveById(cmd.VideoId);
                        MainThread.BeginInvokeOnMainThread(() => QueueUpdated?.Invoke());
                        await WriteJsonResponseAsync(res, 200, new TvCommandResult { Success = removed, Message = removed ? "Vídeo removido da fila." : "Vídeo não encontrado." });
                    }
                    else
                    {
                        await WriteJsonResponseAsync(res, 400, new TvCommandResult { Success = false, Message = "Identificador de vídeo inválido." });
                    }
                    return;
                }

                if (path == "/api/v1/queue/clear")
                {
                    TvQueueManager.Instance.Clear();
                    MainThread.BeginInvokeOnMainThread(() => QueueUpdated?.Invoke());
                    await WriteJsonResponseAsync(res, 200, new TvCommandResult { Success = true, Message = "Fila limpa com sucesso." });
                    return;
                }
            }

            // 6. COMANDOS REMOTOS (POST /api/v1/playback/command)
            if (path == "/api/v1/playback/command" && method == "POST")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var cmd = JsonSerializer.Deserialize<TvRemoteCommand>(body);

                if (cmd != null)
                {
                    if (cmd.Action == "unlock_parent_area" || cmd.Action == "unlock_tv")
                    {
                        ClearAuthPending();
                    }

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        CommandReceived?.Invoke(cmd);
                    });
                    await WriteJsonResponseAsync(res, 200, new TvCommandResult { Success = true, Message = $"Comando '{cmd.Action}' executado." });
                }
                else
                {
                    await WriteJsonResponseAsync(res, 400, new TvCommandResult { Success = false, Message = "Comando inválido." });
                }
                return;
            }

            // 7. SINCRONIZAÇÃO DE DADOS TELEMÓVEL -> TV (POST /api/v1/sync_data)
            if (path == "/api/v1/sync_data" && method == "POST")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var syncPayload = JsonSerializer.Deserialize<TvSyncPayload>(body);

                if (syncPayload != null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        SyncDataReceived?.Invoke(syncPayload);
                    });
                    await WriteJsonResponseAsync(res, 200, new TvCommandResult { Success = true, Message = "Dados sincronizados com sucesso." });
                }
                else
                {
                    await WriteJsonResponseAsync(res, 400, new TvCommandResult { Success = false, Message = "Payload de sincronização inválido." });
                }
                return;
            }

            // 8. ROTA DESCONHECIDA
            await WriteJsonResponseAsync(res, 404, new { error = "Not found" });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TvServer.ProcessRequest] {ex.Message}");
            try { await WriteJsonResponseAsync(res, 500, new { error = ex.Message }); } catch { }
        }
    }

    private static async Task WriteJsonResponseAsync(HttpListenerResponse res, int statusCode, object data)
    {
        res.StatusCode = statusCode;
        res.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        res.Close();
    }
}
