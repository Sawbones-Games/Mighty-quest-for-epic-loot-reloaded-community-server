# MQEL launcher/patcher — findings & final architecture

This documents the investigation behind the patcher: how the cert bypass is delivered, why it's a
self-contained DLL, and why the launcher's own updater can't deliver it on Steam.

---

## TL;DR

- **The server split works** and the **full Steam → launcher → Play → client flow works** (real Steam
  ticket flows through, client signs in, gets its account, loads the lobby).
- The client's HTTPS trust store is **empty** — it rejects every cert (self-signed *and* real, proven on
  the wire). So the client must be told to skip TLS verify. That's a two-byte in-memory patch.
- **The delivery = a self-contained `dinput8.dll` proxy** dropped into `GameData\Bin`. It works from both
  a direct launch and the real Steam launcher (verified: full combat, 0 corruption). Our earlier belief
  that a launcher/steamticket launch tripped an anti-tamper check on the DLL was **wrong** — it was a
  stale left-over "file corruption" dialog from a previous run bleeding across rapid test cycles.
- The patcher makes **only two changes, both inside the game folder**: writes `Bin\dinput8.dll` and edits
  `Launcher\PublicLauncherSettings.json`. Nothing outside the game folder is created/modified/deleted.

---

## The cert bypass (two integrity checks to respect)

```
RVA 0x6219FE : 0F 95 C2 (setnz dl) -> 90 90 90   => SSL_VERIFY_NONE
RVA 0x622FD6 : 74 (JZ)             -> EB          => step3 always "verify ok"
```

1. **Early one-time `.text` unpack/integrity check** — always runs at boot. If the verify bytes are
   already changed when it runs, it detects the tamper and **sabotages the cert** (verify keeps failing
   even though the bytes look patched). So we must patch strictly *after* it. The proxy keys the patch to
   the client's own TLS activity — it watches `Bin\NetworkLog.Txt` and patches the moment the first TLS
   handshake appears (networking always starts after the integrity check). Machine-independent; the
   client's handshake retries then pick up the patched verify.
2. The old "steamticket-gated anti-tamper" theory (that the launcher launch trips a second check) was a
   **measurement error** (stale dialog). There is no second blocker to defeat — the DLL just works.

## Forwarding proxy + patcher-supplied companion (no shipped copyrighted code)

`MightyQuest.exe` **statically imports `DINPUT8.dll` → `DirectInput8Create`** (confirmed from its PE
import table). `dinput8` is a real Windows system DLL used for input, so our proxy can't just replace it —
it has to pass those calls through. The proxy re-exports all 6 real `dinput8` entry points (only
`DirectInput8Create` is used by the game) as **forwarders to `dinput8_orig.dll`** (via `src/dinput8.def`),
built with `zig cc -target x86-windows-gnu`.

A **renamed** companion (`dinput8_orig.dll`) is mandatory. The "cleaner" idea of having the proxy
`LoadLibrary` the real `dinput8.dll` from System32 at runtime **does not work**: a module named
`dinput8.dll` (ours) is already loaded, so the loader keys by base name and returns *us* — the forward
recurses into itself and the game shows **"file corruption detected"**. This is exactly why every proxy
DLL uses a renamed original. (We tried the runtime-load version; it failed. Reverted.)

So we ship only our proxy, and the **patcher creates `dinput8_orig.dll` by copying the user's own OS
`dinput8.dll`** into `Bin` — no copyrighted code shipped, System32/SysWOW64 only read to make the copy.

**Bitness gotcha:** the game is 32-bit, so the companion must be the **32-bit** `dinput8.dll` from
`C:\Windows\SysWOW64` (`Environment.SpecialFolder.SystemX86`), NOT `System32` — on 64-bit Windows
`System32\dinput8.dll` is the *64-bit* DLL and the game fails to load ("game exited unexpectedly").

## Patch timing — ignore stale network-log content

The proxy patches only *after* the one-time integrity check, and detects "the check has run" by watching
`Bin\NetworkLog.Txt` for the first TLS `handshake` (networking always starts after the check). **But that
log is APPENDED across sessions** — stale `handshake` lines from previous runs make the proxy patch
immediately on load, *before* the check, which then flags **"file corruption"**. Fix: the proxy records
`NetworkLog.Txt`'s size at load (`g_baseline`) and only scans bytes written after it — i.e. THIS session's
handshake (with a reset if the game recreates the file). Verified live: with a stale multi-session log
present, the game boots clean and connects (`HTTP/1.1 200 OK`, no corruption).

## Why the launcher's OWN updater can't deliver the DLL (task "B")

The elegant idea was "point the launcher's `PatcherServiceUrl` at us, have it download our DLL into `Bin`,
so the user edits only the JSON." We fully reverse-engineered the patch contract from `PublicLauncher.exe`:

- `RMLauncherAndServerPackagesVersion { RMLauncherPatch, RMServerPackagesVersion }`
- `RMServerPackagesVersion { BranchName, GamePublicationLabel, RMPackagePatches[] }` — the list member is
  **`RMPackagePatches`** (the *request* class `RMClientPackagesVersion` uses `RMPackageVersions`; the
  asymmetry caused a long chase of "Unhandled member" warnings).
- `RMPackagePatch { FullDownloadSize, FullInstallUrl, PatchDownloadSize, PatchInstallUrl,
  RMPackagePatchFlags (int enum), RMPackageVersion }`; flags: `CanInstallFullInstall=1, CanInstallPatch=2,
  ClientVersionUpToDate=4, PackageDeleted=8, UpdateVersionNumberOnly=16`.

Returning a valid `CanInstallFullInstall` GameBin patch parses fine — but the **Steam build refuses to
self-download**. It shows message-box keys 49/50: *"Update needed — You are not running the latest version
of the game. Please update via the Steam client."* The CDN self-patcher (`DownloadPackage`/`InstallPackage`
/`xdelta`/WinHttp) is compiled in from the shared codebase but is unused on the Steam distribution —
game-file updates are delegated to Steam. **So B is infeasible on the Steam build**, and the DLL must be
placed by the patcher instead. (The server's `GetRMLauncherAndPackagesVersion` handler is left reporting
"up to date" so the launcher shows Play with no nag.)

## The server split (kept)

The launcher talks plain **HTTP**; the client uses **HTTPS**. One app listens on `http://:8080` (launcher)
and `https://:8443` (client), routed by path. The patcher writes both into `PublicLauncherSettings.json`.
