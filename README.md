# NOXMFD Extension: Remote Control Missile Camera

[![NOXMFD](https://img.shields.io/badge/Requires-NOXMFD-blue)](https://github.com/roke77/NOXMFD)
[![MissileCamera](https://img.shields.io/badge/Requires-MissileCamera-lightgrey)](https://github.com/Mursisru/MissileCamera)
[![MissileCamera RC](https://img.shields.io/badge/Requires-MissileCamera%20RC-lightgrey)](https://github.com/Mursisru/MissileCamera-Remote-Control)
[![Version](https://img.shields.io/badge/Version-0.1.2-green)](https://github.com/roke77/NOXMFD-Extension-Remote-Control-Missile-Camera/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Adds a **MISSILE CAMERA** page to [NOXMFD](https://github.com/roke77/NOXMFD)'s browser MFD — live seeker MJPEG, HUD markers, telemetry, and remote-piloting controls for missiles under [MissileCamera Remote Control](https://github.com/Mursisru/MissileCamera-Remote-Control).

Built entirely through NOXMFD's public extension API (`NOXMFD.Api`, see [`EXTENSIONS.md`](https://github.com/roke77/NOXMFD/blob/main/EXTENSIONS.md)). This repo does **not** modify NOXMFD's source.

> [!IMPORTANT]
> **Install order:** BepInEx 5 → [MissileCamera](https://github.com/Mursisru/MissileCamera) → [MissileCamera: Remote Control](https://github.com/Mursisru/MissileCamera-Remote-Control) → [NOXMFD](https://github.com/roke77/NOXMFD) (≥ 0.23.0) → **`NOXMFD.RcMissileCamera.dll`**.

---

## Table of contents

- [Features](#features)
- [What's here](#whats-here)
- [Using the page](#using-the-page)
- [Configuration](#configuration)
- [Building](#building)
- [Installing](#installing)
- [Cutting a release](#cutting-a-release)
- [Changelog](#changelog)
- [Credits](#credits)

---

## Features

- **Live seeker MJPEG** at `/ext/rc-missile-camera/feed.mjpg` (only while this page is visible).
- **Headless bridge capture** — drives MissileCamera `McBridge.RequestCapture` so the feed works without cockpit fullscreen (`K`).
- **RC commands** — aim drag, TAKE / RELEASE, throttle nudge, **AB click-toggle**, formation, vision cycle, manual detonate (hold), missile pool picker.
- **Telemetry overlay** — speed, alt, range, fuel, guidance, TTI; CombatHUD unit markers with configurable name labels (via MC bridge cfg).
- **Tab isolation** — MJPEG pauses when you leave the MISSILE CAMERA page so RC does not leak onto MAP / WPN / other NOXMFD tabs.

---

## What's here

Mirrors NOXMFD's own `src/plugin` + `src/web` split.

- `src/plugin/Plugin.cs`, `MissileCameraLifecycle.cs`, `MissileCameraCommands.cs`, `MissileCameraTelemetry.cs`, `MissileCameraAssets.cs` — BepInEx plugin.
- `src/plugin/McBridge.cs`, `RcBridge.cs`, `RcFeed.cs` — reflection soft-deps on MissileCamera and MissileCamera Remote Control (neither mod references this extension).
- `src/web/missile-camera.html`, `missile-camera.css`, `missile-camera.js` — MFD page UI; commands POST to `/ext/rc-missile-camera/command`.
- `lib/NOXMFD.dll` — compile-time reference only (not shipped to players).

---

## Using the page

1. Launch a player-owned missile (same rules as MissileCamera / RC).
2. In NOXMFD, open **EXT → MISSILE CAMERA** (exact nav label depends on your NOXMFD layout).
3. Press **TAKE** to remote-control (requires RC installed and a controllable RC clone unless `AllowAnyMunition` is on).
4. Drag on the feed to aim; use **AB** (toggle), throttle buttons, **FORM**, **VIS**, **DETONATE** (hold) as needed.

> [!TIP]
> Cockpit fullscreen (`K`) is optional — this page is a full RC surface on its own. With MissileCamera `SuppressCockpitMfd=true` (default), the in-cockpit MFD missile panel hides while this page holds bridge capture.

> [!NOTE]
> **Preview-only mode:** if RC is missing or not ready, the page may still show the seeker feed but disables TAKE / control buttons.

---

## Configuration

This extension has **no separate `.cfg` file**. It reads MissileCamera bridge tuning via reflection off `MissileCamera.Bridge.McBridge`'s `Extension*`-prefixed properties:

| `McBridge` property | Effect on this page |
| :--- | :--- |
| `ExtensionStreamHz` | MJPEG capture rate (4–30, default 10) |
| `ExtensionStreamMaxDim` | MJPEG capture size (240–1080px, default 480) |
| `ExtensionStreamJpegQuality` | MJPEG compression quality (20–90, default 42) |
| `ExtensionTelemetryInterval` / `ExtensionMarkersInterval` / `ExtensionPoolInterval` | Browser update rates |

As of MissileCamera 2.1.0-pre.1, these are hardcoded values on the Bridge, not BepInEx `ConfigEntry<T>`s — there is currently no `.cfg` key that changes them, despite the wide clamp ranges above suggesting they were built to be tunable.

---

## Building

Requires the .NET SDK and a Nuclear Option install with BepInEx 5 + NOXMFD already present.

```bash
dotnet build -c Release
```

If your game is not at the default Steam path, create a local `GameDir.props` next to `RcMissileCamera.csproj` (gitignored):

```xml
<Project><PropertyGroup>
  <GameDir>D:\SteamLibrary\steamapps\common\Nuclear Option</GameDir>
</PropertyGroup></Project>
```

Release build copies `NOXMFD.RcMissileCamera.dll` to `<GameDir>\BepInEx\plugins`.

---

## Installing

1. Install **NOXMFD** (≥ 0.23.0), **MissileCamera**, and **MissileCamera Remote Control**.
2. Drop **`NOXMFD.RcMissileCamera.dll`** into `BepInEx/plugins/`. Do **not** copy `lib/NOXMFD.dll` — NOXMFD already loads it.
3. Launch the game. **MISSILE CAMERA** appears under NOXMFD's EXT nav automatically.

---

## Updating `lib/NOXMFD.dll`

Replace `lib/NOXMFD.dll` with a fresh build from [NOXMFD](https://github.com/roke77/NOXMFD) when the `Api` surface changes so this project still compiles.

---

## Cutting a release

- Tag = bare semver (`0.1.0`, not `v0.1.0`). Match `<Version>` in `RcMissileCamera.csproj`.
- Full release only (no `--prerelease`).
- Title: `NOXMFD: RC Missile Camera Extension X.Y.Z`.
- Attach `NOXMFD.RcMissileCamera_X.Y.Z.zip` containing `NOXMFD.RcMissileCamera/NOXMFD.RcMissileCamera.dll`.

```bash
dotnet build -c Release
gh release create X.Y.Z --title "NOXMFD: RC Missile Camera Extension X.Y.Z" --notes-file CHANGELOG.md NOXMFD.RcMissileCamera_X.Y.Z.zip
```

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

---

## Credits

### Thanks to project contributors

[![Contributors](https://contrib.rocks/image?repo=roke77/NOXMFD-Extension-Remote-Control-Missile-Camera)](https://github.com/roke77/NOXMFD-Extension-Remote-Control-Missile-Camera/graphs/contributors)

- **[lupfine](https://github.com/lupfine)** — Original remote-camera / Bridge integration design
- **[Mursisru](https://github.com/Mursisru)** — this extension, MissileCamera, and MissileCamera: Remote Control
- **[roke77](https://github.com/roke77)** — [NOXMFD](https://github.com/roke77/NOXMFD) host and extension API

The Bridge APIs on the MissileCamera and RC side were shaped for external MFD consumers; this repo is a standalone fork of that integration so it ships on its own release cycle instead of living inside NOXMFD's tree.
