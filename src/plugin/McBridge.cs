using System;
using System.Reflection;
using UnityEngine;

namespace RcMissileCamera
{
    // Soft dependency on the BASE "Missile Camera" mod's public Bridge (Bridge/McBridge.cs there) —
    // separate from RcBridge.cs, which talks to MissileCameraRemoteControl (the RC add-on). This one
    // is what makes the /rc feed work WITHOUT the pilot entering fullscreen or having the cockpit MFD
    // panel bound: McBridge.RequestCapture(true) adds NOXMFD as a third legitimate reason for the
    // base mod's feed pipeline to stay live, alongside the cockpit panel and Fullscreen — see that
    // mod's MissileCameraFeedController.IsDisplayPipelineActive / CAMERA_SAFETY.md.
    //
    // Same resolution shape as RcBridge.cs: cached delegates via reflection, retried on a cooldown
    // since plugin load order between mods isn't guaranteed, every member safe/no-op until resolved.
    internal static class McBridge
    {
        private const int MinApiVersion = 1;

        private static bool  _resolved;
        private static float _nextAttempt;

        private static Func<bool>?    _hasTrackableMissile;
        private static Func<Camera>?  _feedCamera;
        private static Func<Texture>? _feedTexture;
        private static Func<string>?  _telemetryJson;
        private static Func<string>?  _markersJson;
        private static Action?        _cycleVisionMode;
        private static Action<bool>?  _requestCapture;
        private static Func<bool>?    _isCaptureActive;
        private static Func<int>?     _streamHz;
        private static Func<int>?     _streamMaxDim;
        private static Func<int>?     _streamJpegQuality;
        private static Func<float>?   _telemetryInterval;
        private static Func<float>?   _markersInterval;
        private static Func<float>?   _poolInterval;

        internal static bool Available => EnsureResolved();

        internal static bool    HasTrackableMissile => Available && _hasTrackableMissile!();
        internal static bool    IsCaptureActive       => Available && _isCaptureActive != null && _isCaptureActive!();

        internal static int  StreamHz           => Available && _streamHz != null ? UnityEngine.Mathf.Clamp(_streamHz(), 4, 30) : 10;
        internal static int  StreamMaxDim         => Available && _streamMaxDim != null ? UnityEngine.Mathf.Clamp(_streamMaxDim(), 240, 1080) : 480;
        internal static int  StreamJpegQuality    => Available && _streamJpegQuality != null ? UnityEngine.Mathf.Clamp(_streamJpegQuality(), 20, 90) : 42;
        internal static float TelemetryInterval   => Available && _telemetryInterval != null ? _telemetryInterval() : 0.15f;
        internal static float MarkersInterval     => Available && _markersInterval != null ? _markersInterval() : 0.2f;
        internal static float PoolInterval        => Available && _poolInterval != null ? _poolInterval() : 0.5f;
        internal static Camera? FeedCamera           => Available ? _feedCamera!() : null;

        // Prefer this over FeedCamera.targetTexture — see McBridge.cs (base mod) for why: the
        // camera's own targetTexture is swapped to an intermediate HDR buffer mid-render and
        // restored afterward, so it doesn't reliably hold the final picture at an arbitrary read
        // time the way this authoritative output texture does.
        internal static Texture? FeedTexture         => Available ? _feedTexture!() : null;

        // Raw JSON straight from the base mod (McBridge.TelemetryJson) — spliced verbatim into our
        // own "rc" telemetry block rather than parsed, since it's already valid, pre-escaped JSON
        // with exactly the fields/formatting we want (see McBridge.cs there for the field list).
        internal static string? TelemetryJson => Available ? _telemetryJson!() : null;

        // Raw JSON array straight from the base mod (McBridge.MarkersJson) — spliced verbatim
        // into the "rc" block's "markers" key, same reasoning as TelemetryJson above. "[]" (not
        // null) when unavailable, since missile-camera.js always expects an array here.
        internal static string MarkersJson => Available ? (_markersJson!() ?? "[]") : "[]";

        // Cycle the Fullscreen-style vision filter (Color/NightVision/WhiteHot/BlackHot/Contour) —
        // current mode's label rides inside TelemetryJson's "visionMode" field, not a separate call.
        internal static void CycleVisionMode() { if (Available) _cycleVisionMode!(); }

        // Level-triggered — call every tick with the current "do I still need frames" state (false
        // once WantsMjpegFrames drops, same as RcFeed already does for its own gating). No-op when
        // the base mod's Bridge isn't present, so callers don't need to guard this themselves.
        internal static void RequestCapture(bool active) { if (Available) _requestCapture!(active); }

        private static bool EnsureResolved()
        {
            if (_resolved) return true;
            if (Time.unscaledTime < _nextAttempt) return false;
            _nextAttempt = Time.unscaledTime + 2f;

            try
            {
                Assembly? asm = null;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name == "MissileCamera") { asm = a; break; }
                }
                if (asm == null) return false;   // base mod not installed — normal, not an error

                Type? t = asm.GetType("MissileCamera.Bridge.McBridge");
                if (t == null)
                {
                    Plugin.Log?.LogWarning("[MISSILE CAMERA] MissileCamera found but has no Bridge — too old? Headless capture disabled (falls back to fullscreen-gated feed).");
                    return false;
                }

                FieldInfo? verField = t.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static);
                int ver = verField != null ? (int)verField.GetValue(null) : 0;
                if (ver < MinApiVersion)
                {
                    Plugin.Log?.LogWarning($"[MISSILE CAMERA] MissileCamera Bridge ApiVersion {ver} < {MinApiVersion} — headless capture disabled.");
                    return false;
                }

                _hasTrackableMissile = BindGet<bool>(t, "HasTrackableMissile");
                _feedCamera          = BindGet<Camera>(t, "FeedCamera");
                _feedTexture         = BindGet<Texture>(t, "FeedTexture");
                _telemetryJson       = BindFunc<string>(t, "TelemetryJson");
                _markersJson         = BindFunc<string>(t, "MarkersJson");
                _cycleVisionMode     = BindAction(t, "CycleVisionMode");
                _requestCapture      = BindAction1<bool>(t, "RequestCapture");
                _isCaptureActive     = BindGet<bool>(t, "IsCaptureActive");
                _streamHz            = BindGet<int>(t, "ExtensionStreamHz");
                _streamMaxDim        = BindGet<int>(t, "ExtensionStreamMaxDim");
                _streamJpegQuality   = BindGet<int>(t, "ExtensionStreamJpegQuality");
                _telemetryInterval   = BindGet<float>(t, "ExtensionTelemetryInterval");
                _markersInterval     = BindGet<float>(t, "ExtensionMarkersInterval");
                _poolInterval        = BindGet<float>(t, "ExtensionPoolInterval");

                _resolved = _hasTrackableMissile != null && _feedCamera != null
                    && _feedTexture != null && _telemetryJson != null && _markersJson != null
                    && _cycleVisionMode != null
                    && _requestCapture != null;
                if (_resolved)
                    Plugin.Log?.LogInfo("[MISSILE CAMERA] MissileCamera Bridge found (v" + ver + ") — headless capture enabled.");
                else
                    Plugin.Log?.LogWarning("[MISSILE CAMERA] MissileCamera Bridge shape mismatch — headless capture disabled.");
                return _resolved;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[MISSILE CAMERA] MissileCamera bridge resolve failed: {ex.Message}");
                return false;
            }
        }

        private static Func<TRet>? BindGet<TRet>(Type t, string propName)
        {
            MethodInfo? get = t.GetProperty(propName, BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
            return get == null ? null : (Func<TRet>)Delegate.CreateDelegate(typeof(Func<TRet>), get);
        }

        private static Func<TRet>? BindFunc<TRet>(Type t, string methodName)
        {
            MethodInfo? m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            return m == null ? null : (Func<TRet>)Delegate.CreateDelegate(typeof(Func<TRet>), m);
        }

        private static Action<T1>? BindAction1<T1>(Type t, string methodName)
        {
            MethodInfo? m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(T1) }, null);
            return m == null ? null : (Action<T1>)Delegate.CreateDelegate(typeof(Action<T1>), m);
        }

        private static Action? BindAction(Type t, string methodName)
        {
            MethodInfo? m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            return m == null ? null : (Action)Delegate.CreateDelegate(typeof(Action), m);
        }
    }
}
