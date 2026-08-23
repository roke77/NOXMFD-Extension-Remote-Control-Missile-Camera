using UnityEngine;

namespace RcMissileCamera
{
    // Per-frame driver, living on its own persistent GameObject (see Plugin.cs's comment on why).
    // Commands don't need draining here — NOXMFD's own MissionLifecycle drains the extension
    // command queue and calls MissileCameraCommands.Handle directly (docs/extensions-api.md
    // surface #3). This only owns the capture feed's tick and the periodic telemetry publish.
    internal class MissileCameraLifecycle : MonoBehaviour
    {
        private readonly RcFeed _feed = new RcFeed();
        private float _telemetryTimer;

        private void Update()
        {
            // Level-triggered bool — keep fresh every frame while the page wants MJPEG.
            bool wants = NOXMFD.Api.WantsMjpegFrames(Plugin.ExtId);
            if (wants && McBridge.Available)
                McBridge.RequestCapture(true);
            else
                McBridge.RequestCapture(false);

            float dt = Time.deltaTime;
            _feed.Tick(dt);

            _telemetryTimer += dt;
            float teleInterval = UnityEngine.Mathf.Clamp(McBridge.TelemetryInterval, 0.05f, 1f);
            if (_telemetryTimer >= teleInterval)
            {
                _telemetryTimer = 0f;
                MissileCameraTelemetry.Publish(_feed);
            }
        }

        private void OnDestroy()
        {
            _feed.Disengage();
        }
    }
}
