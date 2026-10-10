using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using yChicaTube.Services.Remote.Models;

namespace yChicaTube.Services.Remote;

public class TvDiscoveryService
{
    private static readonly Lazy<TvDiscoveryService> _instance = new(() => new TvDiscoveryService());
    public static TvDiscoveryService Instance => _instance.Value;

    public const int DiscoveryPort = 8943;
    public const int HttpPort = 8942;
    private const string DiscoveryMagic = "YCHICATUBE_TV_DISCOVERY";

    private UdpClient? _listener;
    private CancellationTokenSource? _listenerCts;
    private bool _isAdvertising;

    // ==========================================
    // TV SIDE: ANÚNCIO E RESPOSTA UDP
    // ==========================================

    public void StartAdvertising()
    {
        if (_isAdvertising) return;
        _isAdvertising = true;
        _listenerCts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            try
            {
                _listener = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort))
                {
                    EnableBroadcast = true
                };

                var buffer = new byte[1024];
                while (!_listenerCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await _listener.ReceiveAsync(_listenerCts.Token);
                        var message = Encoding.UTF8.GetString(result.Buffer).Trim();

                        if (message.Contains(DiscoveryMagic))
                        {
                            // Responde com o anúncio desta TV
                            var responsePacket = new TvDiscoveryPacket
                            {
                                Type = "YCHICATUBE_TV_ANNOUNCE",
                                TvId = TvPairingManager.Instance.TvId,
                                TvName = TvPairingManager.Instance.TvName,
                                IpAddress = GetLocalIpAddress(),
                                Port = HttpPort,
                                IsPaired = TvPairingManager.Instance.GetPairedClients().Count > 0
                            };

                            var json = JsonSerializer.Serialize(responsePacket);
                            var bytes = Encoding.UTF8.GetBytes(json);
                            await _listener.SendAsync(bytes, bytes.Length, result.RemoteEndPoint);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[TvDiscoveryService.Listener] {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TvDiscoveryService.Start] {ex.Message}");
            }
        });
    }

    public void StopAdvertising()
    {
        _isAdvertising = false;
        try
        {
            _listenerCts?.Cancel();
            _listener?.Close();
            _listener?.Dispose();
            _listener = null;
        }
        catch { }
    }

    // ==========================================
    // MOBILE SIDE: PROCURA DE TVs NA REDE LOCAL
    // ==========================================

    public async Task<List<TvDiscoveryPacket>> DiscoverTvsAsync(int timeoutMs = 2500)
    {
        var discoveredTvs = new Dictionary<string, TvDiscoveryPacket>();
        using var client = new UdpClient();
        client.EnableBroadcast = true;

        var cts = new CancellationTokenSource(timeoutMs);

        // Tarefa de escuta de respostas
        var listenTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var result = await client.ReceiveAsync(cts.Token);
                    var json = Encoding.UTF8.GetString(result.Buffer);
                    var packet = JsonSerializer.Deserialize<TvDiscoveryPacket>(json);

                    if (packet != null && !string.IsNullOrWhiteSpace(packet.TvId))
                    {
                        // Se o IP vier em branco, usa o IP da origem do pacote UDP
                        if (string.IsNullOrWhiteSpace(packet.IpAddress) || packet.IpAddress == "127.0.0.1")
                        {
                            packet.IpAddress = result.RemoteEndPoint.Address.ToString();
                        }

                        lock (discoveredTvs)
                        {
                            discoveredTvs[packet.TvId] = packet;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch { }
            }
        });

        try
        {
            var magicBytes = Encoding.UTF8.GetBytes(DiscoveryMagic);

            // Envia para o broadcast global
            await client.SendAsync(magicBytes, magicBytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

            // Envia para broadcasts específicos de cada interface de rede
            var broadcastAddresses = GetSubnetBroadcastAddresses();
            foreach (var bcast in broadcastAddresses)
            {
                try
                {
                    await client.SendAsync(magicBytes, magicBytes.Length, new IPEndPoint(bcast, DiscoveryPort));
                }
                catch { }
            }

            // Aguarda o timeout
            await Task.Delay(timeoutMs, cts.Token).ContinueWith(_ => { });
        }
        catch { }
        finally
        {
            cts.Cancel();
            try { client.Close(); } catch { }
        }

        try { await listenTask; } catch { }

        lock (discoveredTvs)
        {
            return discoveredTvs.Values.ToList();
        }
    }

    public static string GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint)
            {
                return endPoint.Address.ToString();
            }
        }
        catch { }

        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }
        }
        catch { }

        return "127.0.0.1";
    }

    private static List<IPAddress> GetSubnetBroadcastAddresses()
    {
        var broadcasts = new List<IPAddress>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && ua.IPv4Mask != null)
                    {
                        var ipBytes = ua.Address.GetAddressBytes();
                        var maskBytes = ua.IPv4Mask.GetAddressBytes();
                        var broadcastBytes = new byte[4];
                        for (int i = 0; i < 4; i++)
                        {
                            broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                        }
                        broadcastAddressesSafe(broadcasts, new IPAddress(broadcastBytes));
                    }
                }
            }
        }
        catch { }
        return broadcasts;
    }

    private static void broadcastAddressesSafe(List<IPAddress> list, IPAddress ip)
    {
        if (!list.Any(existing => existing.Equals(ip)))
        {
            list.Add(ip);
        }
    }
}
