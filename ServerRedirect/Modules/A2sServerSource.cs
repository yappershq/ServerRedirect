using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServerRedirect.Configuration;
using SteamQuery;

namespace ServerRedirect.Modules;

/// <summary>Queries each configured server via SteamQuery.NET A2S_INFO.</summary>
internal sealed class A2sServerSource : IServerSource
{
    private readonly A2sConfig _cfg;
    private readonly ILogger   _logger;

    public A2sServerSource(A2sConfig cfg, ILogger logger)
    {
        _cfg    = cfg;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ServerInfo>> FetchAsync(CancellationToken ct = default)
    {
        if (_cfg.Servers.Count == 0)
            return [];

        var tasks = _cfg.Servers.Select(entry => QueryOneAsync(entry, ct)).ToArray();
        var results = await Task.WhenAll(tasks);

        var list = new List<ServerInfo>(_cfg.Servers.Count);
        foreach (var r in results)
        {
            if (r is not null)
                list.Add(r);
        }

        return list;
    }

    private async Task<ServerInfo?> QueryOneAsync(A2sServerEntry entry, CancellationToken ct)
    {
        try
        {
            // Resolve domain if needed so we can do A2S
            var (host, port) = SplitAddress(entry.Address);
            if (port <= 0)
            {
                _logger.LogWarning("[ServerRedirect] A2S: invalid address '{Addr}'", entry.Address);
                return null;
            }

            using var gs = new GameServer(host, port, AddressFamily.InterNetwork);
            gs.SendTimeout = TimeSpan.FromSeconds(5);

            await gs.GetInformationAsync(ct);
            var info = gs.Information;

            return new ServerInfo
            {
                Name    = info.ServerName ?? entry.Name,
                Address = entry.Address,
                Map     = info.Map ?? string.Empty,
                Players = info.OnlinePlayers,
                Max     = info.MaxPlayers,
                Online  = true,
            };
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "[ServerRedirect] A2S query failed for '{Addr}'", entry.Address);
            // Return offline placeholder so it can be merged with the last-good cache
            return new ServerInfo
            {
                Name    = entry.Name,
                Address = entry.Address,
                Online  = false,
            };
        }
    }

    internal static (string host, int port) SplitAddress(string address)
    {
        var idx = address.LastIndexOf(':');
        if (idx < 0 || !int.TryParse(address[(idx + 1)..], out var port))
            return (address, -1);

        return (address[..idx], port);
    }
}
