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

| Module | Required? | Why |
|--------|-----------|-----|
| CommandCenter | ✅ Required | registers the `!servers` / `!redirect` chat commands |
| MenuManager | ✅ Required | renders the in-game server browser menu |
| LocalizerManager | ✅ Required | all player-facing text |
| [Motd](https://github.com/yappershq/Motd) | ⚪ Optional | the "Connect via website" button (opens the connect page). Without it, only the manual-connect option shows |

Bundled: `SteamQuery.NET` (ships with the module; used only in `a2s` mode).

## ⌨️ Commands

Chat commands are configurable (`commands` in the config). Defaults — no permission required:

| Command | Description |
|---------|-------------|
| `!servers` | Open the server browser menu |
| `!redirect` | Alias for the same menu |

The menu is: **server list** (name · players/max · map) → **server info** → **connect** (via website MOTD, or manual console command).

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
| `ad.min_players` | `3` | Skip advertising near-empty servers |
| `ad.order` | `"most_players"` | Which server to advertise: `most_players` / `rotate` / `random` |
| `ad.message_key` | `serverredirect.ad.line` | Locale key for the ad line |

The `api.fields` map is what makes this work against **any** API — remap the keys to match your own JSON shape; the defaults match the cstema.lt schema.

## 🔧 How it works

A background task refreshes the server list every `cache_seconds` from the configured source (HTTP API or A2S). The cache is replaced **only on a successful fetch**, so a web redeploy or a server timeout never empties the in-game menu — players keep seeing the last-known list. The current server is excluded by matching its public IP (`ISteamApi.GetPublicIP()`) + `hostport` against each entry (domain addresses are DNS-resolved). "Connect via website" opens `connect_url` through the Motd module (the page launches `steam://connect/<ip:port>`); "Connect manually" prints the `connect` command to console. All player-facing text goes through LocalizerManager (`locales/serverredirect.json`).

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
