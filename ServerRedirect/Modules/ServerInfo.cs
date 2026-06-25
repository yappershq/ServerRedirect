namespace ServerRedirect.Modules;

/// <summary>Normalized server record returned by any IServerSource implementation.</summary>
internal sealed class ServerInfo
{
    public string Name    { get; init; } = string.Empty;
    public int    Players { get; init; }
    public int    Max     { get; init; }
    public string Map     { get; init; } = string.Empty;
    /// <summary>host:port string, e.g. "1.2.3.4:27015" or "cs2.cstema.lt:27015".</summary>
    public string Address { get; init; } = string.Empty;
    public bool   Online  { get; init; } = true;
}
