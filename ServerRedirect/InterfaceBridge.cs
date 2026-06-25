using System;
using System.Net;
using Microsoft.Extensions.Logging;
using Motd.Shared;
using Sharp.Modules.LocalizerManager.Shared;
using Sharp.Modules.MenuManager.Shared;
using Sharp.Shared;
using Sharp.Shared.Definition;
using Sharp.Shared.Managers;
using Sharp.Shared.Objects;

namespace ServerRedirect;

internal sealed class InterfaceBridge
{
    internal static InterfaceBridge Instance { get; private set; } = null!;

    internal string SharpPath { get; }
    internal string DllPath   { get; }

    internal IModSharp            ModSharp            { get; }
    internal IClientManager       ClientManager       { get; }
    internal IConVarManager       ConVarManager       { get; }
    internal ISharpModuleManager  SharpModuleManager  { get; }
    internal ILoggerFactory       LoggerFactory       { get; }

    internal ILocalizerManager? LocalizerManager { get; private set; }
    internal IMenuManager?      MenuManager      { get; private set; }
    internal IMotdShared?       MotdShared       { get; private set; }

    /// <summary>Our public IP as a dotted string (resolved from SteamGameServer once in OAM).</summary>
    internal string PublicIp { get; private set; } = string.Empty;

    public InterfaceBridge(string dllPath, string sharpPath, ISharedSystem sharedSystem, ILoggerFactory loggerFactory)
    {
        Instance = this;

        SharpPath = sharpPath;
        DllPath   = dllPath;

        ModSharp           = sharedSystem.GetModSharp();
        ClientManager      = sharedSystem.GetClientManager();
        ConVarManager      = sharedSystem.GetConVarManager();
        SharpModuleManager = sharedSystem.GetSharpModuleManager();
        LoggerFactory      = loggerFactory;
    }

    internal void ResolveOptionalModules()
    {
        LocalizerManager ??= SharpModuleManager.GetOptionalSharpModuleInterface<ILocalizerManager>(ILocalizerManager.Identity)?.Instance;
        MenuManager      ??= SharpModuleManager.GetOptionalSharpModuleInterface<IMenuManager>(IMenuManager.Identity)?.Instance;
        MotdShared       ??= SharpModuleManager.GetOptionalSharpModuleInterface<IMotdShared>(IMotdShared.Identity)?.Instance;
    }

    internal void InitLocalizer()
    {
        if (LocalizerManager is not { } lm)
            return;

        try
        {
            lm.LoadLocaleFile("serverredirect", suppressDuplicationWarnings: true);
        }
        catch (Exception e)
        {
            LoggerFactory.CreateLogger<InterfaceBridge>()
                .LogWarning(e, "[ServerRedirect] serverredirect.json locale not found — using key fallbacks.");
        }
    }

    /// <summary>
    /// Resolve our own public IP via ISteamApi. Steam returns it in host byte order on little-endian x64.
    /// <c>BitConverter.GetBytes(uint)</c> on LE gives [LSB...MSB] = network-order dotted-quad,
    /// which <c>new IPAddress(bytes)</c> reads as network byte order. Correct on Linux x64.
    /// </summary>
    internal void ResolvePublicIp(ILogger logger)
    {
        try
        {
            var steam = ModSharp.GetSteamGameServer();
            var raw   = steam.GetPublicIP();
            if (raw == 0)
            {
                logger.LogWarning("[ServerRedirect] GetPublicIP returned 0 — Steam not yet connected? Self-exclude will fall back to port-only.");
                return;
            }

            // On little-endian host, Steam host-order uint: bytes[0]=octet1, bytes[1]=octet2 ...
            // Same layout as what IPAddress ctor expects (network byte order) on LE.
            PublicIp = new IPAddress(BitConverter.GetBytes(raw)).ToString();
            logger.LogInformation("[ServerRedirect] Public IP resolved: {Ip}", PublicIp);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "[ServerRedirect] Failed to resolve public IP — self-exclude may miss.");
        }
    }

    /// <summary>Localize a key, rendering {color} tokens as CHAT control chars. Falls back to the raw key.</summary>
    internal string Localize(IGameClient client, string key, params object?[] args)
        => Render(client, key, html: false, args);

    /// <summary>Localize a key for MENU items, rendering {color} tokens as HTML &lt;font&gt; tags (CS2 menus are center-HTML).</summary>
    internal string LocalizeHtml(IGameClient client, string key, params object?[] args)
        => Render(client, key, html: true, args);

    private string Render(IGameClient client, string key, bool html, object?[] args)
    {
        if (LocalizerManager?.For(client) is not { } locale)
            return key;

        try
        {
            return ApplyColors(locale.Text(key, args), html);
        }
        catch
        {
            return key;
        }
    }

    // Locale stores {{token}}; string.Format un-escapes it to {token}. Chat uses control chars,
    // menus are center-HTML so they need <font color> tags instead. {default} closes/resets.
    private static string ApplyColors(string text, bool html)
    {
        foreach (var (token, chat, htmlTag) in ColorTokens)
            text = text.Replace(token, html ? htmlTag : chat);

        if (html)
        {
            // MenuManager wraps each item in its own <font color='...'>{title}</font>. An unclosed
            // <font> from a token (e.g. a title that opens {green} but never {default}) would eat
            // that wrapper's </font> and bleed into sibling items — so pad the missing closers.
            var unclosed = (text.Split("<font ").Length - 1) - (text.Split("</font>").Length - 1);
            for (var i = 0; i < unclosed; i++)
                text += "</font>";
        }

        return text;
    }

    private static readonly (string token, string chat, string html)[] ColorTokens =
    [
        ("{default}",    ChatColor.White,      "</font>"),
        ("{white}",      ChatColor.White,      "<font color='#ffffff'>"),
        ("{darkred}",    ChatColor.DarkRed,    "<font color='#8b0000'>"),
        ("{pink}",       ChatColor.Pink,       "<font color='#ff69b4'>"),
        ("{green}",      ChatColor.Green,      "<font color='#40ff40'>"),
        ("{lightgreen}", ChatColor.LightGreen, "<font color='#99ff99'>"),
        ("{lime}",       ChatColor.Lime,       "<font color='#00ff00'>"),
        ("{red}",        ChatColor.Red,        "<font color='#ff4040'>"),
        ("{grey}",       ChatColor.Grey,       "<font color='#cccccc'>"),
        ("{gray}",       ChatColor.Grey,       "<font color='#cccccc'>"),
        ("{yellow}",     ChatColor.Yellow,     "<font color='#ffff00'>"),
        ("{gold}",       ChatColor.Gold,       "<font color='#ffd700'>"),
        ("{silver}",     ChatColor.Silver,     "<font color='#c0c0c0'>"),
        ("{blue}",       ChatColor.Blue,       "<font color='#6699ff'>"),
        ("{darkblue}",   ChatColor.DarkBlue,   "<font color='#2222ff'>"),
        ("{purple}",     ChatColor.Purple,     "<font color='#b266ff'>"),
        ("{lightred}",   ChatColor.LightRed,   "<font color='#ff8080'>"),
    ];
}
