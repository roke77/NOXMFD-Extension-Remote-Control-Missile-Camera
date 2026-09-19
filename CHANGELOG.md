# Changelog

All notable changes to **NOXMFD: RC Missile Camera Extension** are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [0.1.4] — 2026-09-19

### Fixed

- **RC commands silently rejected** — `postCmd()` posted to `/ext/rc-missile-camera/command` without a `Content-Type` header, so the browser defaulted to `text/plain`. NOXMFD's command endpoint has required an exact `application/json` Content-Type since its 2026-08-27 hardening, so every command (TAKE, RELEASE, aim, throttle, AB, FORM, VIS, DETONATE) was being rejected with 415 and silently dropped. Fixed by explicitly setting `Content-Type: application/json` on the request.

## [0.1.3] — 2026-09-19

### Changed

- **RcFeed video pipeline** — migrated JPEG compression to a dedicated background worker thread ("RcMissileCamera JPEG") using `AutoResetEvent` signalling, matching the proven core `TgpFeed` architecture.
- **Frame buffer capture** — eliminated managed `Texture2D` allocations, texture data uploads, and synchronous encoding in favour of a fast `NativeArray` memory copy directly upon readback completion.

### Fixed

- **Cyclic frame time stutter** — eliminated periodic rendering freezes and garbage collection pauses during active weapon tracking and remote camera broadcasts.
- **Lifecycle and thread termination** — added generation staleness guards (`_captureGeneration`) with `InvalidatePendingWork()` to discard orphaned frames across session resets, and bound `MissileCameraLifecycle.OnDestroy` to safely terminate the background encoder thread.

## [0.1.2] — 2026-08-23

### Added

- **Tab-aware MJPEG** — pauses `/ext/rc-missile-camera/feed.mjpg` when the page iframe is hidden so bridge capture and RC UI stay on the MISSILE CAMERA tab only.
- **Bridge tuning via reflection** — reads MissileCamera `[MissileCameraBridge]` settings through `McBridge` (stream rate, JPEG size, telemetry/marker intervals).
- **16:9-friendly page layout** — feed uses the full iframe content area with letterboxed overlays (reticle, markers).

### Changed

- **AB (afterburner)** — click-toggle on the NOXMFD page (cockpit FS still uses hold Left Shift).
- Telemetry `fsActive` treats `McBridge.IsCaptureActive` as camera-active for headless bridge use.

### Fixed

- MJPEG stall reconnect watchdog; preview-only UI when RC is unavailable.
- Letterbox-correct marker and reticle placement on resized panes.

## [0.1.1] — 2026-08-20

- Pinned NOXMFD minimum version; hardened feed reconnect and DETONATE hold against dropped connections.

## [0.1.0] — 2026-08-15

- Initial standalone extension: MISSILE CAMERA page, MJPEG feed, RC command POST endpoint, telemetry slice.
