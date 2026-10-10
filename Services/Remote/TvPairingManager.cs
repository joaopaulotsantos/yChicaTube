using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Maui.Storage;
using yChicaTube.Services.Remote.Models;

namespace yChicaTube.Services.Remote;

public class TvPairingManager
{
    private static readonly Lazy<TvPairingManager> _instance = new(() => new TvPairingManager());
    public static TvPairingManager Instance => _instance.Value;

    private const string PairedClientsKey = "tv_paired_clients_v1";
    private const string PairedTvsKey = "mobile_paired_tvs_v1";
    private const string TvIdKey = "tv_device_unique_id";
    private const string TvNameKey = "tv_device_custom_name";
    private const string ClientDeviceIdKey = "mobile_client_unique_id";

    private string? _currentPairingCode;
    private DateTime _pairingCodeExpiresAt = DateTime.MinValue;
    private readonly object _lock = new();

    public string TvId
    {
        get
        {
            var id = Preferences.Default.Get(TvIdKey, string.Empty);
            if (string.IsNullOrWhiteSpace(id))
            {
                id = "tv-" + Guid.NewGuid().ToString("N")[..12];
                Preferences.Default.Set(TvIdKey, id);
            }
            return id;
        }
    }

    public string TvName
    {
        get
        {
            var name = Preferences.Default.Get(TvNameKey, string.Empty);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = string.IsNullOrWhiteSpace(DeviceInfo.Name) ? "yChicaTube TV" : $"yChicaTube ({DeviceInfo.Name})";
                Preferences.Default.Set(TvNameKey, name);
            }
            return name;
        }
        set
        {
            Preferences.Default.Set(TvNameKey, value);
        }
    }

    public string ClientDeviceId
    {
        get
        {
            var id = Preferences.Default.Get(ClientDeviceIdKey, string.Empty);
            if (string.IsNullOrWhiteSpace(id))
            {
                id = "phone-" + Guid.NewGuid().ToString("N")[..12];
                Preferences.Default.Set(ClientDeviceIdKey, id);
            }
            return id;
        }
    }

    public string ClientDeviceName => string.IsNullOrWhiteSpace(DeviceInfo.Name) ? "Telemóvel" : DeviceInfo.Name;

    // ==========================================
    // TV SIDE: GERAÇÃO E VALIDAÇÃO DE CÓDIGO
    // ==========================================

    public (string code, int remainingSeconds) GenerateNewPairingCode()
    {
        lock (_lock)
        {
            var randomNum = RandomNumberGenerator.GetInt32(100000, 999999);
            _currentPairingCode = randomNum.ToString();
            _pairingCodeExpiresAt = DateTime.UtcNow.AddMinutes(5);
            return (FormattedCurrentCode, 300);
        }
    }

    public string FormattedCurrentCode
    {
        get
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(_currentPairingCode) || DateTime.UtcNow > _pairingCodeExpiresAt)
                {
                    return string.Empty;
                }
                // Ex: "583 214"
                return $"{_currentPairingCode[..3]} {_currentPairingCode[3..]}";
            }
        }
    }

    public int RemainingCodeSeconds
    {
        get
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(_currentPairingCode) || DateTime.UtcNow > _pairingCodeExpiresAt)
                    return 0;
                return Math.Max(0, (int)(_pairingCodeExpiresAt - DateTime.UtcNow).TotalSeconds);
            }
        }
    }

    public bool ValidateAndPairClient(TvPairRequest request, out string authToken, out string errorMessage)
    {
        authToken = string.Empty;
        errorMessage = string.Empty;

        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(_currentPairingCode) || DateTime.UtcNow > _pairingCodeExpiresAt)
            {
                errorMessage = "O código de emparelhamento expirou. Gere um novo código na TV.";
                return false;
            }

            var cleanInput = request.PairingCode.Replace(" ", "").Trim();
            if (!string.Equals(cleanInput, _currentPairingCode, StringComparison.Ordinal))
            {
                errorMessage = "Código de emparelhamento incorreto.";
                return false;
            }

            // Invalida o código após uso bem-sucedido
            _currentPairingCode = null;
            _pairingCodeExpiresAt = DateTime.MinValue;

            // Gera token seguro de 256 bits
            var tokenBytes = RandomNumberGenerator.GetBytes(32);
            authToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();

            // Regista o dispositivo cliente autorizado
            var clients = GetPairedClients();
            clients.RemoveAll(c => c.DeviceId == request.ClientDeviceId);
            clients.Add(new PairedClientInfo
            {
                DeviceId = request.ClientDeviceId,
                DeviceName = string.IsNullOrWhiteSpace(request.ClientDeviceName) ? "Telemóvel Autorizado" : request.ClientDeviceName,
                AuthToken = authToken,
                PairedAt = DateTime.UtcNow
            });
            SavePairedClients(clients);

            return true;
        }
    }

    public bool IsTokenAuthorized(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        var cleanToken = token.Trim();
        if (cleanToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            cleanToken = cleanToken[7..].Trim();
        }

        var clients = GetPairedClients();
        return clients.Any(c => string.Equals(c.AuthToken, cleanToken, StringComparison.OrdinalIgnoreCase));
    }

    public List<PairedClientInfo> GetPairedClients()
    {
        try
        {
            var json = Preferences.Default.Get(PairedClientsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json)) return new List<PairedClientInfo>();
            return JsonSerializer.Deserialize<List<PairedClientInfo>>(json) ?? new List<PairedClientInfo>();
        }
        catch
        {
            return new List<PairedClientInfo>();
        }
    }

    public void SavePairedClients(List<PairedClientInfo> clients)
    {
        try
        {
            var json = JsonSerializer.Serialize(clients);
            Preferences.Default.Set(PairedClientsKey, json);
        }
        catch { }
    }

    public void RemovePairedClient(string deviceId)
    {
        var clients = GetPairedClients();
        clients.RemoveAll(c => c.DeviceId == deviceId);
        SavePairedClients(clients);
    }

    public void ClearAllPairedClients()
    {
        Preferences.Default.Remove(PairedClientsKey);
    }

    // ==========================================
    // MOBILE SIDE: GESTÃO DE TVs EMPARELHADAS
    // ==========================================

    public List<TvDevice> GetPairedTvs()
    {
        try
        {
            var json = Preferences.Default.Get(PairedTvsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json)) return new List<TvDevice>();
            return JsonSerializer.Deserialize<List<TvDevice>>(json) ?? new List<TvDevice>();
        }
        catch
        {
            return new List<TvDevice>();
        }
    }

    public void SavePairedTvs(List<TvDevice> tvs)
    {
        try
        {
            var json = JsonSerializer.Serialize(tvs);
            Preferences.Default.Set(PairedTvsKey, json);
        }
        catch { }
    }

    public void AddOrUpdatePairedTv(TvDevice tv)
    {
        var list = GetPairedTvs();
        list.RemoveAll(t => t.Id == tv.Id || (t.IpAddress == tv.IpAddress && t.Port == tv.Port));
        list.Add(tv);
        SavePairedTvs(list);
    }

    public void RemovePairedTv(string tvId)
    {
        var list = GetPairedTvs();
        list.RemoveAll(t => t.Id == tvId);
        SavePairedTvs(list);
    }

    public void RenamePairedTv(string tvId, string newName)
    {
        var list = GetPairedTvs();
        var tv = list.FirstOrDefault(t => t.Id == tvId);
        if (tv != null)
        {
            tv.Name = newName;
            SavePairedTvs(list);
        }
    }
}
