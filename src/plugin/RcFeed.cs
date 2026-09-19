using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace RcMissileCamera
{
    // The MissileCamera: Remote Control feed — same continuous-capture pipeline as NOXMFD's own
    // TgpFeed.cs, but sourced from RcBridge.FeedCamera (a soft dependency on another mod) instead
    // of the game's own TargetCam, and pushed to NOXMFD's generic extension MJPEG surface
    // (Api.PushMjpegFrame — docs/extensions-api.md) instead of a page-specific one.
    //
    // A plain object (not a MonoBehaviour), owned by MissileCameraLifecycle exactly like NOXMFD's
    // TgpFeed is owned by TelemetryReader: driven via Tick(dt) each frame, Active read by
    // MissileCameraTelemetry, torn down from MissileCameraLifecycle.OnDestroy.
    //
    // JPEG encoding runs on a dedicated background thread (same pattern as TgpFeed.EncoderLoop)
    // to avoid stalling the Unity main thread with the synchronous CPU encode.
    internal class RcFeed
    {
        private  const int   FallbackMaxDim      = 480;
        private  const int   FallbackJpegQuality = 42;

        private float          _timer;
        private RenderTexture? _rt;
        private bool           _engaged;
        private volatile bool  _active;
        private bool           _srcLogged;
        private bool           _pixelDiagLogged;
        private bool           _hadSrc;
        private bool           _readbackInFlight;
        private int            _captureGeneration;

        // ── Background JPEG encoder (same pattern as TgpFeed.cs) ────────────────────
        private readonly object         _encoderGate   = new object();
        private readonly AutoResetEvent _encoderSignal = new AutoResetEvent(false);
        private EncodeWork?             _pendingEncode;
        private bool                    _encoderStarted;
        private volatile bool           _encoderStopping;
        private int                     _encoderDrops;

        public bool Active => _active;

        public void Tick(float dt)
        {
            float interval = 1f / UnityEngine.Mathf.Max(McBridge.StreamHz, 4);
            _timer += dt;
            if (_timer < interval) return;
            _timer = 0f;
            CaptureFrame();
        }

        private int MaxDim => McBridge.Available ? McBridge.StreamMaxDim : FallbackMaxDim;
        private int JpegQuality => McBridge.Available ? McBridge.StreamJpegQuality : FallbackJpegQuality;

        private void CaptureFrame()
        {
            // Gate on /ext/rc-missile-camera/feed.mjpg subscribers — same reasoning as TgpFeed: no
            // point reading RcBridge or touching a Camera every tick when no client has the
            // MISSILE CAMERA page open. This gate is also what drives McBridge.RequestCapture
            // below — see that call for why it must run even on the "no subscribers" branch, not
            // just here.
            if (!NOXMFD.Api.WantsMjpegFrames(Plugin.ExtId))
            {
                if (_engaged) Disengage();
                return;
            }

            if (!McBridge.Available)
            {
                NOXMFD.Api.ClearMjpegFrame(Plugin.ExtId);
                _active = false;
                return;
            }

            // Prefer the base mod's own Bridge texture (works headless, per above — and is the
            // actual authoritative output, not read off the camera mid-render — see McBridge.cs).
            // Fall back to RcBridge's fullscreen-gated camera.targetTexture for older MissileCamera
            // installs without a Bridge — same picture, just requires the pilot to actually be in
            // fullscreen for it to be non-null/valid.
            Texture? src;
            bool haveCam;   // only meaningful on the fallback path — see the enabled check below
            if (McBridge.Available)
            {
                src = McBridge.FeedTexture;
                haveCam = src != null || McBridge.HasTrackableMissile;
            }
            else
            {
                Camera? cam = RcBridge.FeedCamera;
                haveCam = cam != null && cam.enabled;
                src = haveCam ? cam!.targetTexture : null;
            }
            if (!haveCam || src == null)
            {
                NOXMFD.Api.ClearMjpegFrame(Plugin.ExtId);
                _active = false;
                if (_hadSrc)
                {
                    // Transition log (not a one-shot like _srcLogged below) — if capture silently
                    // stops mid-session, this is what tells us when/why on the next log, instead of
                    // total silence after the one-time startup dump.
                    _hadSrc = false;
                    Plugin.Log?.LogWarning("[MISSILE CAMERA] feed became unavailable "
                        + $"(McBridge.Available={McBridge.Available}, HasTrackableMissile={McBridge.HasTrackableMissile}, "
                        + $"haveCam={haveCam}).");
                }
                return;
            }
            if (!_hadSrc)
            {
                _hadSrc = true;
                Plugin.Log?.LogInfo("[MISSILE CAMERA] feed (re)available.");
            }

            // Match the captured frame to the source's aspect ratio — see TgpFeed for why (avoids
            // squashing a wider-than-tall feed; the MFD page letterboxes with object-fit:contain).
            int sw = Mathf.Max(1, src.width);
            int sh = Mathf.Max(1, src.height);
            int targetW, targetH;
            int maxSide = Mathf.Max(sw, sh);
            int capMax = MaxDim;
            if (maxSide <= capMax)
            {
                targetW = sw; targetH = sh;
            }
            else if (sw >= sh)
            {
                targetW = capMax;
                targetH = Mathf.Max(1, Mathf.RoundToInt(capMax * (float)sh / sw));
            }
            else
            {
                targetH = capMax;
                targetW = Mathf.Max(1, Mathf.RoundToInt(capMax * (float)sw / sh));
            }

            if (!_srcLogged)
            {
                _srcLogged = true;
                Plugin.Log?.LogInfo($"[MISSILE CAMERA] source texture {sw}x{sh} (aspect {(float)sw / sh:0.000}); capturing at {targetW}x{targetH}.");

                // Diagnostic: camera state itself, when we have one to inspect (McBridge only
                // exposes the texture, so fetch its Camera separately just for this one-time log —
                // harmless extra reflection call, not on the hot path since _srcLogged guards it).
                Camera? diagCam = McBridge.Available ? McBridge.FeedCamera : null;
                if (diagCam != null)
                {
                    Plugin.Log?.LogInfo($"[MISSILE CAMERA] feed camera diag: enabled={diagCam.enabled}, "
                        + $"cullingMask={diagCam.cullingMask}, clip=[{diagCam.nearClipPlane:0.###},{diagCam.farClipPlane:0.#}], "
                        + $"fov={diagCam.fieldOfView:0.#}, targetTexture={(diagCam.targetTexture != null ? diagCam.targetTexture.GetInstanceID().ToString() : "null")}, "
                        + $"feedTexture={src.GetInstanceID()}, sameRT={(diagCam.targetTexture == src)}.");
                }
            }

            // Don't stack readbacks — drop this tick if the GPU is still finishing the last one.
            if (_readbackInFlight) return;

            if (_rt == null || _rt.width != targetW || _rt.height != targetH)
            {
                if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); }
                _rt = new RenderTexture(targetW, targetH, 0, RenderTextureFormat.ARGB32);
                _rt.Create();
            }

            // GPU downscale, then ASYNC readback. AsyncGPUReadback dispatches the readback to the
            // GPU and returns immediately — no main-thread stall. The callback fires on the main
            // thread once the GPU has the bytes ready (typically 1–3 frames later); we then copy
            // the request-owned bytes for the background encoder worker.
            Graphics.Blit(src, _rt);
            _readbackInFlight = true;
            int captureW = targetW;
            int captureH = targetH;
            int captureGeneration = _captureGeneration;
            int jpegQuality = JpegQuality;
            AsyncGPUReadback.Request(_rt, 0, request =>
                OnReadbackComplete(request, captureW, captureH, jpegQuality, captureGeneration));
        }

        // The callback copies request-owned NativeArray memory into a managed byte[] before
        // returning, then hands ownership to the bounded encoder worker. No Texture2D needed —
        // EncodeArrayToJPG works directly on the raw byte array. At most one frame waits behind
        // the frame currently encoding; newer arrivals replace the pending slot.
        private void OnReadbackComplete(AsyncGPUReadbackRequest request, int w, int h,
                                        int jpegQuality, int captureGeneration)
        {
            _readbackInFlight = false;
            if (captureGeneration != _captureGeneration) return;       // stale — Disengage'd
            if (request.hasError)
            {
                _active = false;
                return;
            }
            if (!NOXMFD.Api.WantsMjpegFrames(Plugin.ExtId)) return;    // disengaged while in flight

            try
            {
                var data = request.GetData<byte>();

                // One-time diagnostic: is the captured buffer actually black, or does it have real
                // content that's getting lost downstream (JPEG encode / MJPEG serving / browser)?
                // Sampled every 97th byte (prime, avoids landing on the same channel every time) so
                // this stays cheap even though it only ever runs once per RcFeed instance.
                if (!_pixelDiagLogged)
                {
                    _pixelDiagLogged = true;
                    long sum = 0; int n = 0;
                    for (int i = 0; i < data.Length; i += 97) { sum += data[i]; n++; }
                    double avg = n > 0 ? (double)sum / n : -1;
                    Plugin.Log?.LogInfo($"[MISSILE CAMERA] frame diag: {w}x{h}, avg sampled byte ≈ {avg:0.0} (0=black, 255=white/full).");
                }

                // Fast copy from NativeArray into a managed byte[] for the background thread.
                // ToArray() allocates a new byte[] each frame — acceptable because this replaces
                // the old Texture2D.LoadRawTextureData + Apply + EncodeToJPG pipeline that was
                // far more expensive, and the encoder thread will consume it promptly.
                byte[] pixels = data.ToArray();
                EnqueueEncode(new EncodeWork(pixels, w, h, jpegQuality, captureGeneration));
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[MISSILE CAMERA] readback copy failed: {ex}");
            }
        }

        // ── Background JPEG encoder — mirrors TgpFeed.EncoderLoop exactly ───────────

        private void EnqueueEncode(EncodeWork work)
        {
            bool startWorker = false;
            lock (_encoderGate)
            {
                if (_encoderStopping) return;
                if (_pendingEncode != null)
                    Interlocked.Increment(ref _encoderDrops);
                _pendingEncode = work;
                if (!_encoderStarted)
                {
                    _encoderStarted = true;
                    startWorker = true;
                }
            }
            if (startWorker)
            {
                var worker = new Thread(EncoderLoop)
                {
                    IsBackground = true,
                    Name = "RcMissileCamera JPEG",
                };
                try { worker.Start(); }
                catch (Exception ex)
                {
                    lock (_encoderGate) { _encoderStarted = false; _pendingEncode = null; }
                    Plugin.Log?.LogWarning($"[MISSILE CAMERA] JPEG worker failed to start: {ex}");
                    return;
                }
            }
            _encoderSignal.Set();
        }

        private void EncoderLoop()
        {
            while (true)
            {
                _encoderSignal.WaitOne();
                if (_encoderStopping) return;

                while (true)
                {
                    EncodeWork work;
                    lock (_encoderGate)
                    {
                        if (_pendingEncode == null) break;
                        work = _pendingEncode;
                        _pendingEncode = null;
                    }

                    try
                    {
                        // Stale check — a Disengage or settings change since this was enqueued.
                        if (work.CaptureGeneration != Volatile.Read(ref _captureGeneration) ||
                            !NOXMFD.Api.WantsMjpegFrames(Plugin.ExtId))
                            continue;

                        byte[] jpg = ImageConversion.EncodeArrayToJPG(
                            work.Data, GraphicsFormat.R8G8B8A8_UNorm,
                            (uint)work.Width, (uint)work.Height, 0, work.JpegQuality);

                        // Re-check after encode (could have taken a few ms).
                        if (work.CaptureGeneration != Volatile.Read(ref _captureGeneration) ||
                            !NOXMFD.Api.WantsMjpegFrames(Plugin.ExtId))
                            continue;

                        NOXMFD.Api.PushMjpegFrame(Plugin.ExtId, jpg);
                        _active  = true;
                        _engaged = true;
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log?.LogWarning($"[MISSILE CAMERA] JPEG encode failed: {ex.Message}");
                    }
                }
            }
        }

        private sealed class EncodeWork
        {
            internal EncodeWork(byte[] data, int width, int height, int jpegQuality,
                                int captureGeneration)
            {
                Data = data;
                Width = width;
                Height = height;
                JpegQuality = jpegQuality;
                CaptureGeneration = captureGeneration;
            }

            internal byte[] Data { get; }
            internal int Width { get; }
            internal int Height { get; }
            internal int JpegQuality { get; }
            internal int CaptureGeneration { get; }
        }

        // ── Lifecycle ───────────────────────────────────────────────────────────────

        public void Disengage()
        {
            McBridge.RequestCapture(false);   // e.g. OnDestroy calling this directly, not via CaptureFrame
            if (_rt  != null) { _rt.Release();  UnityEngine.Object.Destroy(_rt);  _rt  = null; }

            bool wasEngaged   = _engaged;
            _engaged          = false;
            _active           = false;
            _srcLogged        = false;
            _hadSrc           = false;
            _pixelDiagLogged  = false;
            InvalidatePendingWork();
            NOXMFD.Api.ClearMjpegFrame(Plugin.ExtId);
            if (wasEngaged)
                Plugin.Log?.LogInfo($"[MISSILE CAMERA] disengaged (no subscribers, encoderDrops={Volatile.Read(ref _encoderDrops)}).");
        }

        /// <summary>
        /// Full teardown including encoder thread shutdown. Call from OnDestroy only — not from
        /// the "no subscribers" path (Disengage), because re-spawning a thread for the next
        /// engage would cost more than just letting it idle on the AutoResetEvent.
        /// </summary>
        public void Shutdown()
        {
            Disengage();
            _encoderStopping = true;
            _encoderSignal.Set();
        }

        private void InvalidatePendingWork()
        {
            Interlocked.Increment(ref _captureGeneration);
            _readbackInFlight = false;
            lock (_encoderGate) _pendingEncode = null;
        }
    }
}
