using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServerRedirect.Configuration;

namespace ServerRedirect.Modules;

/// <summary>Fetches server list from a JSON HTTP API endpoint.</summary>
internal sealed class ApiServerSource : IServerSource
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly ApiConfig  _cfg;
    private readonly ILogger    _logger;

    public ApiServerSource(ApiConfig cfg, ILogger logger)
    {
        _cfg    = cfg;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ServerInfo>> FetchAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_cfg.Url))
            return [];

        try
        {
            var json = await Http.GetStringAsync(_cfg.Url, ct);
            return Parse(json);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "[ServerRedirect] API fetch failed ({Url})", _cfg.Url);
            return [];
        }
    }

    private IReadOnlyList<ServerInfo> Parse(string json)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("[ServerRedirect] API response root is not a JSON array");
            return [];
        }

        var f    = _cfg.Fields;
        var list = new List<ServerInfo>();

        foreach (var el in root.EnumerateArray())
        {
            try
            {
                var name    = GetString(el, f.Name);
                var address = GetString(el, f.Address);
                var map     = GetString(el, f.Map);
                var players = GetInt(el, f.Players);
                var max     = GetInt(el, f.Max);
                var online  = GetBool(el, f.Online, defaultValue: true);
                var names   = GetPlayerNames(el, f.PlayerList, f.PlayerListName);

                list.Add(new ServerInfo
                {
                    Name        = name,
                    Address     = address,
                    Map         = map,
                    Players     = players,
                    Max         = max,
                    Online      = online,
                    PlayerNames = names,
                });
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "[ServerRedirect] Skipping malformed API entry");
            }
        }

        return list;
    }

    private static string GetString(JsonElement el, string key)
        => el.TryGetProperty(key, out var p) ? p.GetString() ?? string.Empty : string.Empty;

    private static int GetInt(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var p)) return 0;
        return p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;
    }

    private static bool GetBool(JsonElement el, string key, bool defaultValue)
    {
        if (!el.TryGetProperty(key, out var p)) return defaultValue;
        if (p.ValueKind == JsonValueKind.True)  return true;
        if (p.ValueKind == JsonValueKind.False) return false;
        return defaultValue;
    }

    // Roster: an array of objects ({name,...}) or plain strings. Names are sanitized of < > so a
    // player can't break the center-HTML menu markup.
    private static IReadOnlyList<string> GetPlayerNames(JsonElement el, string arrayKey, string nameKey)
    {
        if (string.IsNullOrEmpty(arrayKey)
            || !el.TryGetProperty(arrayKey, out var arr)
            || arr.ValueKind != JsonValueKind.Array)
            return [];

        var names = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            string? n = item.ValueKind switch
            {
                JsonValueKind.String                                                        => item.GetString(),
                JsonValueKind.Object when item.TryGetProperty(nameKey, out var p)            => p.GetString(),
                _                                                                           => null,
            };
            if (!string.IsNullOrWhiteSpace(n))
                names.Add(n.Replace("<", string.Empty).Replace(">", string.Empty));
        }
        return names;
    }
}
