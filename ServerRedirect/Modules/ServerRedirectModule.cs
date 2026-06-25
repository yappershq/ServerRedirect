using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Motd.Shared;
using ServerRedirect.Configuration;
using Sharp.Modules.CommandCenter.Shared;
using Sharp.Modules.MenuManager.Shared;
using Sharp.Shared.Enums;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using Sharp.Shared.Units;

namespace ServerRedirect.Modules;

/// <summary>
/// Core module: server list menu, advertisement timer, leave-announce.
/// </summary>
internal sealed class ServerRedirectModule : IModule, IClientListener
{
    private const string ModuleId = "ServerRedirect";

    private readonly InterfaceBridge              _bridge;
    private readonly ServerRedirectConfig         _config;
    private readonly ILogger<ServerRedirectModule> _logger;

    private ServerCache? _cache;

    // Leave-announce: slot → (serverName, chooseTime)
    private readonly (string name, DateTime time)?[] _leaveStash = new (string, DateTime)?[64];

    private Guid _adTimer = Guid.Empty;
    private int  _adRotateIndex;

    int IClientListener.ListenerVersion  => IClientListener.ApiVersion;
    int IClientListener.ListenerPriority => 0;

    public ServerRedirectModule(InterfaceBridge bridge, ServerRedirectConfig config, ILogger<ServerRedirectModule> logger)
    {
        _bridge = bridge;
        _config = config;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────

    public bool Init()
    {
        _bridge.ClientManager.InstallClientListener(this);
        return true;
    }

    public void OnAllSharpModulesLoaded()
    {
        _bridge.ResolveOptionalModules();
        _bridge.InitLocalizer();
        _bridge.ResolvePublicIp(_logger);

        // Build server source
        IServerSource source = _config.DataSource.Equals("a2s", StringComparison.OrdinalIgnoreCase)
            ? new A2sServerSource(_config.A2s, _logger)
            : new ApiServerSource(_config.Api, _logger);

        _cache = new ServerCache(source, _config, _bridge, _bridge.LoggerFactory.CreateLogger<ServerCache>());
        _cache.Start();

        RegisterCommands();
        StartAdTimer();

        _logger.LogInformation("[ServerRedirect] Loaded (source={Src}, commands={Cmds}, ad={Ad}, Menu={Menu}, Motd={Motd})",
            _config.DataSource,
            string.Join(", ", _config.Commands),
            _config.Ad.Enabled,
            _bridge.MenuManager is not null,
            _bridge.MotdShared is not null);
    }

    public void Shutdown()
    {
        if (_adTimer != Guid.Empty)
        {
            _bridge.ModSharp.StopTimer(_adTimer);
            _adTimer = Guid.Empty;
        }

        _cache?.Stop();
        _cache?.Dispose();
        _bridge.ClientManager.RemoveClientListener(this);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Commands

    private void RegisterCommands()
    {
        var cc = _bridge.SharpModuleManager
            .GetOptionalSharpModuleInterface<ICommandCenter>(ICommandCenter.Identity)?.Instance;

        if (cc is null)
        {
            _logger.LogWarning("[ServerRedirect] CommandCenter not present — commands unavailable");
            return;
        }

        var registry = cc.GetRegistry(ModuleId);
        foreach (var cmd in _config.Commands)
            registry.RegisterClientCommand(cmd, OnServersCommand);
    }

    private void OnServersCommand(IGameClient client, StringCommand command)
    {
        if (client.IsFakeClient) return;

        var (servers, hasEver) = GetVisibleServers();

        if (!hasEver)
        {
            client.Print(HudPrintChannel.Chat, _bridge.Localize(client, "serverredirect.unavailable"));
            return;
        }

        // Optional argument: `!servers <name>` filters by server name (case-insensitive substring).
        // One match → jump straight to that server; several → show the filtered list; none → notice.
        IReadOnlyList<ServerInfo> shown = servers;
        ServerInfo?               jumpTo = null;
        if (command.ArgCount >= 1)
        {
            var query   = command.GetArg(1);
            var matches = servers.Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
            {
                client.Print(HudPrintChannel.Chat, _bridge.Localize(client, "serverredirect.no_match", query));
                return;
            }

            shown = matches;
            if (matches.Count == 1)
                jumpTo = matches[0];
        }

        if (_bridge.MenuManager is not { } mm)
        {
            // No menu manager — print list to chat
            PrintServerList(client, shown);
            return;
        }

        mm.DisplayMenu(client, jumpTo is { } target
            ? BuildServerInfoMenu(target)
            : BuildServerListMenu(shown));
    }

    private void PrintServerList(IGameClient client, IReadOnlyList<ServerInfo> servers)
    {
        if (servers.Count == 0)
        {
            client.Print(HudPrintChannel.Chat, _bridge.Localize(client, "serverredirect.no_servers"));
            return;
        }

        client.Print(HudPrintChannel.Chat, _bridge.Localize(client, "serverredirect.list_header"));
        foreach (var s in servers)
        {
            client.Print(HudPrintChannel.Chat,
                _bridge.Localize(client, "serverredirect.list_entry", s.Name, s.Players, s.Max, s.Map));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Menus

    private Menu BuildServerListMenu(IReadOnlyList<ServerInfo> servers)
    {
        var menu = new Menu();
        menu.SetTitle(c => _bridge.LocalizeHtml(c, "serverredirect.menu.title"));

        if (servers.Count == 0)
        {
            menu.AddDisabledItem(c => _bridge.Localize(c, "serverredirect.no_servers"));
        }
        else
        {
            foreach (var server in servers)
            {
                var captured = server;
                menu.AddItem(
                    c => _bridge.LocalizeHtml(c, "serverredirect.menu.server_entry",
                        captured.Name, captured.Players, captured.Max, captured.Map),
                    ctrl => ctrl.Next(BuildServerInfoMenu(captured)));
            }
        }

        menu.AddExitItem(c => _bridge.LocalizeHtml(c, "serverredirect.menu.exit"));
        return menu;
    }

    private Menu BuildServerInfoMenu(ServerInfo server)
    {
        var menu = new Menu();
        // The API's `players` count and `playerList` can disagree (a cstema.lt data bug) — show the
        // roster size we actually list so the title never says "0/15" above a visible player.
        var count = server.PlayerNames.Count > 0 ? server.PlayerNames.Count : server.Players;
        // Stats in the title so they're always visible; the page only fits 5 body items, so we keep
        // those for the action + as many player names as possible (prefix: connect first, then players).
        menu.SetTitle($"{server.Name}   {count}/{server.Max}   {server.Map}".TrimEnd());

        // Numbers off (the player rows aren't real options) — only the Connect line carries a manual "1.".
        menu.SetShowIndex(false);

        // Connect first (selectable) — the cursor lands here, so it stays at the top of the page view.
        menu.AddItem(
            c => _bridge.LocalizeHtml(c, "serverredirect.menu.connect"),
            ctrl =>
            {
                var client   = ctrl.Client;
                var captured = server;
                StashLeave(client, captured.Name);

                if (_bridge.MotdShared is { } motd)
                {
                    var url = _config.ConnectUrl.Replace("{address}", captured.Address);
                    motd.ShowMotd(client, MotdContent.ForUrl(url));
                    client.Print(HudPrintChannel.Chat,
                        _bridge.Localize(client, "serverredirect.connect_web_hint", captured.Name));
                }
                else
                {
                    client.Print(HudPrintChannel.Chat,
                        _bridge.Localize(client, "serverredirect.connect_hint", captured.Address));
                }

                ctrl.Exit();
            });

        // Colored header to set the roster apart from the action, then every player as a selectable
        // entry. They must be AddItem (not disabled) so the cursor can page through them — the menu is
        // scrollable via F3/F4. Selecting a name is a no-op.
        if (server.PlayerNames.Count > 0)
        {
            menu.AddDisabledItem(c => _bridge.LocalizeHtml(c, "serverredirect.menu.players_header"));
            foreach (var pn in server.PlayerNames)
            {
                var name = pn;
                menu.AddItem(c => _bridge.LocalizeHtml(c, "serverredirect.menu.player_entry", name), _ => { });
            }
        }

        menu.AddBackItem(c => _bridge.LocalizeHtml(c, "serverredirect.menu.back"));
        menu.AddExitItem(c => _bridge.LocalizeHtml(c, "serverredirect.menu.exit"));
        return menu;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Leave-announce

    private void StashLeave(IGameClient client, string serverName)
    {
        var slot = client.Slot.AsPrimitive();
        if (slot < 64)
            _leaveStash[slot] = (serverName, DateTime.UtcNow);
    }

    void IClientListener.OnClientDisconnected(IGameClient client, NetworkDisconnectionReason reason)
    {
        var slot = client.Slot.AsPrimitive();
        if (slot >= 64) return;

        var stash = _leaveStash[slot];
        _leaveStash[slot] = null;

        if (stash is not { } entry)
            return;

        // Only broadcast if < 30s since they chose to connect
        if ((DateTime.UtcNow - entry.time).TotalSeconds > 30)
            return;

        var playerName = client.Name ?? "Player";

        // Broadcast to all in-game clients
        foreach (var c in _bridge.ClientManager.GetGameClients(inGame: true))
        {
            if (c.IsFakeClient || c.IsHltv) continue;
            c.Print(HudPrintChannel.Chat,
                _bridge.Localize(c, "serverredirect.leave_announce", playerName, entry.name));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Advertisement

    private void StartAdTimer()
    {
        if (!_config.Ad.Enabled || _config.Ad.IntervalSeconds <= 0)
            return;

        _adTimer = _bridge.ModSharp.PushTimer(
            OnAdTimer,
            _config.Ad.IntervalSeconds,
            GameTimerFlags.Repeatable);
    }

    private void OnAdTimer()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Get candidate: non-self, online, above min_players
                var (servers, hasEver) = GetVisibleServers();
                if (!hasEver || servers.Count == 0)
                    return;

                var candidates = servers
                    .Where(s => s.Online && s.Players >= _config.Ad.MinPlayers)
                    .ToList();

                if (candidates.Count == 0)
                    return;

                ServerInfo pick = _config.Ad.Order switch
                {
                    "rotate" => PickRotate(candidates),
                    "random" => candidates[Random.Shared.Next(candidates.Count)],
                    _        => candidates.OrderByDescending(s => s.Players).First(), // most_players
                };

                // Tell players to type `!servers <name>` rather than dumping a URL — the sub-command
                // jumps straight to that server's connect menu. First word of the name matches the filter.
                var firstCmd = _config.Commands.Count > 0 ? _config.Commands[0] : "servers";
                var joinCmd  = $"{firstCmd} {pick.Name.Split(' ')[0].ToLowerInvariant()}";

                _bridge.ModSharp.InvokeFrameAction(() =>
                {
                    foreach (var c in _bridge.ClientManager.GetGameClients(inGame: true))
                    {
                        if (c.IsFakeClient || c.IsHltv) continue;
                        c.Print(HudPrintChannel.Chat,
                            _bridge.Localize(c, _config.Ad.MessageKey,
                                pick.Name, pick.Players, pick.Max, pick.Map, joinCmd));
                    }
                });
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "[ServerRedirect] Ad timer error");
            }

            await Task.CompletedTask;
        });
    }

    private ServerInfo PickRotate(IReadOnlyList<ServerInfo> candidates)
    {
        var idx = _adRotateIndex % candidates.Count;
        _adRotateIndex++;
        return candidates[idx];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers

    /// <summary>Returns filtered (online, non-self) servers from cache, plus hasEver flag.</summary>
    private (IReadOnlyList<ServerInfo> servers, bool hasEver) GetVisibleServers()
    {
        if (_cache is null)
            return ([], false);

        var (all, hasEver) = _cache.GetCached();
        if (!hasEver)
            return ([], false);

        var visible = all
            .Where(s => s.Online && !_cache.IsSelf(s.Address))
            .ToList();

        return (visible, true);
    }
}
