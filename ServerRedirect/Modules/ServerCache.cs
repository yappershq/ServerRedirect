using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServerRedirect.Configuration;

namespace ServerRedirect.Modules;

/// <summary>
/// Background cache: fetches from the configured source on a timer, keeps the last-good list.
/// On ANY fetch failure the existing cache is preserved (never cleared after the first success).
/// </summary>
internal sealed class ServerCache : IDisposable
{
    private readonly IServerSource           _source;
    private readonly ServerRedirectConfig    _config;
    private readonly InterfaceBridge         _bridge;
    private readonly ILogger<ServerCache>    _logger;

    // Guarded by _lock
    private readonly object                  _lock        = new();
    private IReadOnlyList<ServerInfo>        _cached      = [];
    private DateTime                         _lastSuccess = DateTime.MinValue;
    private bool                             _hasEverSucceeded;

    private CancellationTokenSource? _cts;
    private Task?                    _refreshTask;

    public ServerCache(IServerSource source, ServerRedirectConfig config, InterfaceBridge bridge, ILogger<ServerCache> logger)
    {
        _source  = source;
        _config  = config;
        _bridge  = bridge;
        _logger  = logger;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _refreshTask = Task.Run(() => RefreshLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _refreshTask?.Wait(TimeSpan.FromSeconds(3)); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Returns the current cached server list filtered by excludeSelf config.
    /// Returns an empty list if no successful fetch has occurred yet.
    /// </summary>
    public (IReadOnlyList<ServerInfo> servers, bool hasEver) GetCached()
    {
        lock (_lock)
            return (_cached, _hasEverSucceeded);
    }

    // ───────────────────────────────────────────────────────────────────────────

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        // Initial fetch immediately
        await FetchAndUpdateAsync(ct);

        var interval = TimeSpan.FromSeconds(Math.Max(5, _config.CacheSeconds));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await FetchAndUpdateAsync(ct);
        }
    }

    private async Task FetchAndUpdateAsync(CancellationToken ct)
    {
        try
        {
            var result = await _source.FetchAsync(ct);

            // Only replace cache if we got a valid non-empty result
            if (result is { Count: > 0 })
            {
                lock (_lock)
                {
                    _cached           = result;
                    _lastSuccess      = DateTime.UtcNow;
                    _hasEverSucceeded = true;
                }

                _logger.LogDebug("[ServerRedirect] Cache refreshed: {Count} servers", result.Count);
            }
            else
            {
                _logger.LogDebug("[ServerRedirect] Fetch returned empty list — keeping existing cache (last success: {Last})",
                    _lastSuccess == DateTime.MinValue ? "never" : _lastSuccess.ToString("HH:mm:ss"));
            }
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(e, "[ServerRedirect] Fetch failed — keeping existing cache (last success: {Last})",
                _lastSuccess == DateTime.MinValue ? "never" : _lastSuccess.ToString("HH:mm:ss"));
        }
    }

    public void Dispose() => Stop();

    // ───── Self-exclude helper ───────────────────────────────────────────────

    /// <summary>
    /// Returns true if <paramref name="address"/> identifies this server and should be excluded.
    /// Checks config.self_address (explicit) first; falls back to public-IP + hostport convar.
    /// Domain names in the address are resolved via DNS (on caller thread — should be background).
    /// </summary>
    public bool IsSelf(string address)
    {
        if (!_config.ExcludeSelf)
            return false;

        // Explicit override wins
        if (!string.IsNullOrWhiteSpace(_config.SelfAddress))
            return string.Equals(address, _config.SelfAddress, StringComparison.OrdinalIgnoreCase);

        // Auto-detect: our public IP + hostport
        var ourIp   = _bridge.PublicIp;
        var hostCv  = _bridge.ConVarManager.FindConVar("hostport");
        var ourPort = hostCv?.GetString() ?? "27015";

        if (string.IsNullOrEmpty(ourIp))
            return false; // can't auto-detect

        var (host, port) = A2sServerSource.SplitAddress(address);
        if (port <= 0)
            return false;

        if (ourPort != port.ToString())
            return false;

        // If it's already an IP, compare directly
        if (IPAddress.TryParse(host, out var entryIp))
            return string.Equals(entryIp.ToString(), ourIp, StringComparison.Ordinal);

        // Domain — resolve via DNS (blocking; must be called from background thread)
        try
        {
            var addresses = Dns.GetHostAddresses(host);
            foreach (var a in addresses)
            {
                if (string.Equals(a.ToString(), ourIp, StringComparison.Ordinal))
                    return true;
            }
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "[ServerRedirect] DNS resolve failed for '{Host}' during self-check", host);
        }

        return false;
    }
}
