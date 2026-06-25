<div align="center">
  <h1><strong>ServerRedirect</strong></h1>
  <p>In-game server browser for CS2 — list your other servers with live player counts, and let players jump between them.</p>
</div>

<p align="center">
  <a href="https://github.com/Kxnrl/modsharp-public"><img src="https://img.shields.io/badge/framework-ModSharp-5865F2?logo=github" alt="ModSharp"></a>
  <img src="https://img.shields.io/badge/game-CS2-orange" alt="CS2">
  <img src="https://img.shields.io/github/stars/yappershq/ServerRedirect?style=flat&logo=github" alt="Stars">
</p>

---

A ModSharp plugin that shows players an in-game menu of your other servers — live player counts, current map, and a one-click way to connect. It pulls the server list either from an HTTP JSON API or directly via Steam A2S queries, and can periodically advertise your busiest server in chat. The advertisement + leave-announce behaviour is a CS2/ModSharp port of [GAMMACASE/ServerRedirect](https://github.com/GAMMACASE/ServerRedirect).

## 🚀 Install

Copy the build output into your ModSharp install (`<sharp>` = your `sharp` directory):

| From | To |
|------|----|
| `.build/modules/ServerRedirect/` | `<sharp>/modules/ServerRedirect/` |
| `.build/locales/serverredirect.json` | `<sharp>/locales/serverredirect.json` |
| `.build/configs/serverredirect.jsonc.example` | `<sharp>/configs/serverredirect.jsonc` |

Rename the shipped `.jsonc.example` to `.jsonc`, edit it, and restart the server (or change map) to load.

## 🧩 Dependencies

Uses the **ModSharp first-party modules** (ship with ModSharp): **CommandCenter** (commands), **MenuManager** (menu), **LocalizerManager** (text).

External plugins:

| Plugin | Required? | Why |
|--------|-----------|-----|
| [Motd](https://github.com/yappershq/Motd) | ⚪ Optional | the "Connect via website" button (opens the connect page). Without it, only the manual-connect option shows |

Bundled: `SteamQuery.NET` (ships inside the module; used only in `a2s` mode).

## ⌨️ Commands

Chat commands are configurable (`commands` in the config). Defaults — no permission required:

| Command | Description |
|---------|-------------|
| `!servers` | Open the server browser menu |
| `!redirect` | Alias for the same menu |
| `!servers <name>` | Jump straight to a server by name (e.g. `!servers mix`) |

With no argument, the menu is **server list** (name · players/max · map) → **server info** → **connect** (via website MOTD, or manual console command). With a `<name>` argument, the name is matched (case-insensitive substring): a single match opens that server's connect options directly, several show the filtered list, none shows a notice.

## ⚙️ Configuration

`configs/serverredirect.jsonc`:

| Setting | Default | Meaning |
|---------|---------|---------|
| `commands` | `["servers","redirect"]` | Chat commands that open the menu |
| `cache_seconds` | `30` | Background refresh interval for the server list |
| `exclude_self` | `true` | Hide the current server from the list |
| `self_address` | `""` | Explicit `host:port` for self-detection; empty = auto (public IP + `hostport`, domains resolved) |
| `data_source` | `"api"` | `"api"` (HTTP JSON) or `"a2s"` (Steam A2S queries) |
| `api.url` | `https://cstema.lt/api/servers` | JSON endpoint returning the server array (point at your own) |
| `api.fields` | *(see file)* | Maps your API's JSON keys → the fields the menu needs (`name`, `players`, `max`, `map`, `address`, `online`) |
| `a2s.servers` | `[ … ]` | Servers to A2S-query (`name` + `address`) when `data_source` is `a2s` |
| `connect_url` | `https://cstema.lt/connect/{address}` | URL opened by the "Connect via website" MOTD; `{address}` is substituted |
| `ad.enabled` | `true` | Periodic chat advertisement |
| `ad.interval_seconds` | `180` | How often to advertise |
| `ad.min_players` | `1` | Skip advertising servers below this player count |
| `ad.order` | `"rotate"` | Which server to advertise: `rotate` / `most_players` / `random` |
| `ad.message_key` | `serverredirect.ad.line` | Locale key for the ad line |

The `api.fields` map is what makes this work against **any** API — remap the keys to match your own JSON shape; the defaults match the cstema.lt schema.

## 🔧 How it works

A background task refreshes the server list every `cache_seconds` from the configured source (HTTP API or A2S). The cache is replaced **only on a successful fetch**, so a web redeploy or a server timeout never empties the in-game menu — players keep seeing the last-known list. The current server is excluded by matching its public IP (`ISteamApi.GetPublicIP()`) + `hostport` against each entry (domain addresses are DNS-resolved). "Connect via website" opens `connect_url` through the Motd module (the page launches `steam://connect/<ip:port>`); "Connect manually" prints the `connect` command to console. All player-facing text goes through LocalizerManager (`locales/serverredirect.json`).

## 🔌 Connecting players (the connect page)

> **Operators must host this** — the plugin can't join a player directly.

CS2's in-game panel can only open a **URL**, not run a `connect`. So "Connect via website" opens your `connect_url` (default `https://cstema.lt/connect/{address}`, where `{address}` is the target server's `host:port`) in the MOTD panel, and **you host a page at that URL that bounces the browser to `steam://connect/<ip:port>`**. Steam picks up the `steam://` scheme and joins the player.

Point `connect_url` at a page you control that, given `{address}` (`host:port`):

1. splits off the host and port,
2. resolves the host to an IP (`steam://connect/` wants an address — skip if it's already an IP),
3. redirects the browser to `steam://connect/<ip>:<port>`.

Minimal reference (the cstema.lt page, Next.js):

```tsx
// pages/connect/[address].tsx
export const getServerSideProps = async ({ params }) => {
  const [host, port = "27015"] = params.address.split(":");
  const ip = await dnsLookup(host);                      // steam:// wants an address
  return { props: { steamUrl: `steam://connect/${ip}:${port}` } };
};
// client: hand the steam:// URL to the browser, plus a manual fallback link
useEffect(() => { window.location.href = steamUrl; }, [steamUrl]);
```

A static host works too — even `<meta http-equiv="refresh" content="0; url=steam://connect/IP:PORT">` does the job. The only requirement is that visiting the URL hands a `steam://…` to the browser.

### Which `steam://` URL?

There are two forms — pick whichever you prefer:

| URL | Behaviour |
|-----|-----------|
| `steam://connect/<ip>:<port>` | Connects an **already-running** game. Wants an **IP**, so resolve the host first. |
| `steam://run/730//+connect%20<host>:<port>` | **Launches CS2** (app `730`) with a `+connect` launch arg, then connects — works even if the game isn't open. Accepts a **hostname** (the game resolves it), so **no DNS lookup needed**. Note the URL-encoded space (`%20`) between `+connect` and the address. |

The `run/730` form is the more robust one (launches the client and skips the DNS step) — e.g. `steam://run/730//+connect%20cs2.cstema.lt:27016`. Either is fine; just hand it to the browser the same way (`window.location` / `<meta refresh>` / a link).

If you don't host such a page (or don't install [Motd](https://github.com/yappershq/Motd)), players use **"Connect manually"**, which prints `connect <ip:port>` to their console instead.

## 📦 Build

```bash
dotnet build -c Release
```

Outputs `.build/modules/ServerRedirect/ServerRedirect.dll` plus the bundled `locales/` and `configs/` assets.

## 🙏 Credits

Advertisement + leave-announce behaviour ported from [GAMMACASE/ServerRedirect](https://github.com/GAMMACASE/ServerRedirect) (SourceMod). The in-game connect flow uses the website's `steam://connect` page instead of the CSGO-era redirect trick.

---

<div align="center">
  Made with ❤️ by <strong>yappershq</strong> · ⭐ the repo if you find it useful
</div>
