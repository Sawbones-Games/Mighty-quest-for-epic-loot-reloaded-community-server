# MQEL Patcher

Makes **The Mighty Quest for Epic Loot** launch straight from Steam against a private server — no
per-launch scripts, no manual file juggling.

The patcher makes exactly **two changes, both inside the game folder** — nothing outside it is ever
touched (see [FINDINGS.md](FINDINGS.md) for the full investigation):

1. Drops our self-contained cert-bypass proxy → `GameData\Bin\dinput8.dll`
2. Points the launcher config → `Launcher\PublicLauncherSettings.json` at your server

Then you just press **Play** in Steam as normal.

## Why a DLL

The client connects to the gameserver over **HTTPS**, but its trust store is empty — it rejects *every*
certificate, including real ones (proven on the wire). So the client has to be told to skip TLS verify.
The cleanest way is a **`dinput8.dll` proxy**: `MightyQuest.exe` statically imports `dinput8`, and
Windows' DLL search loads *our* `dinput8.dll` (sitting next to the exe) first. Ours applies a two-byte
TLS-verify bypass in memory as the process starts:

```
RVA 0x6219FE : 0F 95 C2 (setnz dl) -> 90 90 90   => SSL_VERIFY_NONE
RVA 0x622FD6 : 74 (JZ)             -> EB          => step3 always "verify ok"
```

It patches **after** the game's one-time `.text` integrity check — it waits for *this session's* first TLS
handshake in `NetworkLog.Txt` (baselining the log at load so stale handshakes from previous runs don't
make it patch early). Patch too early and the check flags "file corruption"; this timing is
machine-independent, no fixed timers.

`dinput8` is a real Windows DLL the game uses for input, so our proxy forwards those 6 calls to a
**renamed companion `dinput8_orig.dll`** (a proxy can't forward to `dinput8.dll` — same base name as
itself — without recursing). The patcher creates that companion by copying the user's **own** OS
`dinput8.dll`, so we ship only our proxy — no copyrighted code, and nothing outside the game folder is
modified (System32/SysWOW64 is only *read* to make the local copy). The game is 32-bit, so the companion
is the 32-bit DLL from `SysWOW64`.

## Usage

**Double-click `MqelPatcher.exe`** for a small UI: it auto-detects the game folder, has fields for the
**Launcher URL** and **Server URL**, and **Install** / **Uninstall** buttons (Uninstall reverts
everything to stock). Or the command line:

```
MqelPatcher                                        launch the UI
MqelPatcher --launcher-url URL --server-url URL    install
MqelPatcher --uninstall                            remove the DLL + restore the original config
MqelPatcher "PATH\TO\GAME"                         explicit game folder if auto-detect fails

defaults: --launcher-url http://localhost:8080  --server-url https://localhost:8443
```

`MqelPatcher.exe` is a self-contained single file (bundles the .NET runtime and our `dinput8.dll`) —
nothing to install on the target machine. Uninstall deletes `Bin\dinput8.dll` and restores the launcher
config from the backup the patcher made on first install (`PublicLauncherSettings.mqel-backup.json`).

## Server (why the URLs are split)

The launcher talks **plain HTTP** (its "no certs" pre-game handshake); the game client uses **HTTPS**.
They can't share a port with different schemes, so the server listens on `http://:8080` (launcher) **and**
`https://:8443` (client), one app routed by path. The patcher writes both into the config:

| Config key | Value |
|---|---|
| `LauncherWebSiteUrl` | `http://HOST:8080/mqel-live/launcher/load/` |
| `GameServerUrl` | `https://HOST:8443/mqel-live.gameserver` |
| `PatcherServiceUrl` | `http://HOST:8080/mqel-live.distribution` |
| `BackgroundImageUrl` | `http://HOST:8080/static/empty.png` |
| `MaintenanceModeVerificationUrl` | `http://HOST:8080/mqel-live/launcher/` |

For a public host, just set the two URLs to your domain, e.g. `--launcher-url http://mqel.example.com`
and `--server-url https://mqel.example.com` (a reverse proxy on 80/443 in front of the split 8080/8443).

## Layout

```
tools/mqel-patcher/
  installer/            # THE tool — MqelPatcher.exe: UI + CLI; embeds + installs the proxy, edits the config
  src/, dinput8.dll     # the dinput8 proxy (cert bypass; forwards input to dinput8_orig.dll)
  build-dll.sh          # rebuilds dinput8.dll (requires zig)
  README.md, FINDINGS.md
```

## Build

1. `bash build-dll.sh` → `dinput8.dll` (32-bit proxy; requires zig).
2. `dotnet publish -c Release` in `installer/` → `bin/Release/.../publish/MqelPatcher.exe`
   (self-contained, single file; the freshly-built `dinput8.dll` is embedded as a resource).

## Notes / follow-ups

- `err60:2` may appear in `NetworkLog.Txt` before the patch lands (a couple of handshakes fail, then the
  client's retries succeed) — cosmetic; the game connects and plays fine.
- Steam "verify integrity of game files" removes the extra `dinput8.dll` and reverts the config — just
  re-run the patcher afterwards.
- Delivering the DLL through the launcher's *own* updater (so the user would edit only the JSON) is **not
  possible on the Steam build** — it defers all game-file updates to Steam ("update via the Steam client").
  See FINDINGS.md.
