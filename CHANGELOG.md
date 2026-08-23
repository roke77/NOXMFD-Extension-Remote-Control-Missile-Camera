# Changelog

All notable changes to **NOXMFD: RC Missile Camera Extension** are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

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
