using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ServerRedirect.Configuration;

internal sealed class ServerRedirectConfig
{
    [JsonPropertyName("commands")]
    public List<string> Commands { get; set; } = ["servers", "redirect"];

    [JsonPropertyName("cache_seconds")]
    public int CacheSeconds { get; set; } = 30;

    [JsonPropertyName("exclude_self")]
    public bool ExcludeSelf { get; set; } = true;

    /// <summary>Optional explicit self address (e.g. "1.2.3.4:27015"). Empty = auto-detect.</summary>
    [JsonPropertyName("self_address")]
    public string SelfAddress { get; set; } = string.Empty;

    /// <summary>"api" or "a2s"</summary>
    [JsonPropertyName("data_source")]
    public string DataSource { get; set; } = "api";

    [JsonPropertyName("api")]
    public ApiConfig Api { get; set; } = new();

    [JsonPropertyName("a2s")]
    public A2sConfig A2s { get; set; } = new();

    /// <summary>URL template with {address} placeholder for the connect website link.</summary>
    [JsonPropertyName("connect_url")]
    public string ConnectUrl { get; set; } = "https://cstema.lt/connect/{address}";

    [JsonPropertyName("ad")]
    public AdConfig Ad { get; set; } = new();

    // ──────────────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
        WriteIndented               = true,
    };

    public static ServerRedirectConfig Load(string sharpPath, ILogger logger)
    {
        var path = Path.Combine(sharpPath, "configs", "serverredirect.json");
        try
        {
            if (!File.Exists(path))
            {
                logger.LogInformation("[ServerRedirect] No config at {Path} — using defaults", path);
                return new ServerRedirectConfig();
            }

            var cfg = JsonSerializer.Deserialize<ServerRedirectConfig>(File.ReadAllText(path), JsonOpts);
            if (cfg is null)
            {
                logger.LogError("[ServerRedirect] serverredirect.json deserialized to null, using defaults");
                return new ServerRedirectConfig();
            }

            logger.LogInformation("[ServerRedirect] Loaded config from {Path} (source={Src})", path, cfg.DataSource);
            return cfg;
        }
        catch (Exception e)
        {
            logger.LogError(e, "[ServerRedirect] Failed to load serverredirect.json, using defaults");
            return new ServerRedirectConfig();
        }
    }
}

internal sealed class ApiConfig
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "https://cstema.lt/api/servers";

    /// <summary>Mapping from our ServerInfo field name to the JSON key in the API response.</summary>
    [JsonPropertyName("fields")]
    public ApiFieldMap Fields { get; set; } = new();
}

internal sealed class ApiFieldMap
{
    [JsonPropertyName("name")]    public string Name    { get; set; } = "name";
    [JsonPropertyName("players")] public string Players { get; set; } = "players";
    [JsonPropertyName("max")]     public string Max     { get; set; } = "maxPlayers";
    [JsonPropertyName("map")]     public string Map     { get; set; } = "map";
    [JsonPropertyName("address")] public string Address { get; set; } = "address";
    [JsonPropertyName("online")]  public string Online  { get; set; } = "online";

    /// <summary>JSON key of the player-roster array (e.g. "playerList"). Empty disables the roster.</summary>
    [JsonPropertyName("player_list")]      public string PlayerList     { get; set; } = "playerList";
    /// <summary>Key holding each player's name inside a roster entry (e.g. "name").</summary>
    [JsonPropertyName("player_list_name")] public string PlayerListName { get; set; } = "name";
}

internal sealed class A2sConfig
{
    [JsonPropertyName("servers")]
    public List<A2sServerEntry> Servers { get; set; } = [];
}

internal sealed class A2sServerEntry
{
    [JsonPropertyName("name")]    public string Name    { get; set; } = string.Empty;
    [JsonPropertyName("address")] public string Address { get; set; } = string.Empty;
}

internal sealed class AdConfig
{
    [JsonPropertyName("enabled")]          public bool   Enabled         { get; set; } = true;
    [JsonPropertyName("interval_seconds")] public int    IntervalSeconds { get; set; } = 180;
    [JsonPropertyName("min_players")]      public int    MinPlayers      { get; set; } = 1;
    /// <summary>"most_players" | "rotate" | "random"</summary>
    [JsonPropertyName("order")]            public string Order           { get; set; } = "rotate";
    [JsonPropertyName("message_key")]      public string MessageKey      { get; set; } = "serverredirect.ad.line";
}
