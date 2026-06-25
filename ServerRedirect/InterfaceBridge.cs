using System;
using System.Net;
using Microsoft.Extensions.Logging;
using Motd.Shared;
using Sharp.Modules.LocalizerManager.Shared;
using Sharp.Modules.MenuManager.Shared;
using Sharp.Shared;
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

    /// <summary>Localize a key for a client. Falls back to the raw key on any failure.</summary>
    internal string Localize(IGameClient client, string key, params object?[] args)
    {
        if (LocalizerManager?.For(client) is not { } locale)
            return key;

        try
        {
            var text = locale.Text(key, args);
            return text;
        }
        catch
        {
            return key;
        }
    }
}
