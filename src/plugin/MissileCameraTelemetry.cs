using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RcMissileCamera
{
    // Builds the same status block NOXMFD's own TelemetryServer.RcBlock used to build
    // server-side (PR #45), now owned entirely by this extension and pushed via
    // Api.PublishSlice instead of spliced into NOXMFD's own frame builder.
    //
    // ponytail: the aim reticle rides this normal 10 Hz slice (aimX/aimY fields) rather than its
    // own high-rate SSE channel the way PR #45's version did — Api.PublishEvent exists for that,
    // but no browser page currently subscribes to a per-extension high-rate event automatically
    // (docs/extensions-api.md, Deferred). Ceiling: the reticle updates at 10 Hz instead of ~60 Hz,
    // which will read as slightly less smooth during a fast drag. Upgrade path: once the
    // browser-side generic listener registration lands, switch this back to PublishEvent.
    internal static class MissileCameraTelemetry
    {
        private static float _nextMarkersTime;
        private static float _nextPoolTime;
        private static string _cachedMarkers = "[]";
        private static IReadOnlyList<string> _cachedPool = System.Array.Empty<string>();

        internal static void Publish(RcFeed feed)
        {
            NOXMFD.Api.PublishSlice(Plugin.ExtId, Build(feed));
        }

        private static string Build(RcFeed feed)
        {
            bool mc = McBridge.Available;
            bool rc = RcBridge.Available;
            if (!mc && !rc)
                return "{\"available\":false}";

            // Pipeline gate (TAKE / "camera active") — bridge capture counts, not cockpit MFD overlay.
            bool fsActive = McBridge.IsCaptureActive
                || (rc ? RcBridge.IsFullscreenActive : McBridge.HasTrackableMissile);

            // "tele"/"markers" are spliced in verbatim (see McBridge.TelemetryJson/MarkersJson)
            string tele = string.IsNullOrEmpty(McBridge.TelemetryJson) ? "null" : McBridge.TelemetryJson!;

            float now = UnityEngine.Time.unscaledTime;
            if (now >= _nextMarkersTime)
            {
                _cachedMarkers = string.IsNullOrEmpty(McBridge.MarkersJson) ? "[]" : McBridge.MarkersJson;
                _nextMarkersTime = now + UnityEngine.Mathf.Clamp(McBridge.MarkersInterval, 0.05f, 1f);
            }

            if (rc && now >= _nextPoolTime)
            {
                _cachedPool = RcBridge.ControllablePool;
                _nextPoolTime = now + UnityEngine.Mathf.Clamp(McBridge.PoolInterval, 0.1f, 2f);
            }

            string markers = _cachedMarkers;
            UnityEngine.Vector2 reticle = rc ? RcBridge.ReticleViewport : new UnityEngine.Vector2(0.5f, 0.5f);

            bool controlling = rc && RcBridge.IsControlling;
            if (!McBridge.HasTrackableMissile && !controlling)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{{\"available\":true,\"rcReady\":{0},\"fsActive\":false,\"hasFrame\":false," +
                    "\"controlling\":false,\"missile\":\"\",\"thr\":0,\"boost\":false,\"link\":\"\"," +
                    "\"formation\":false,\"pool\":[],\"aimX\":0.5,\"aimY\":0.5,\"tele\":null,\"markers\":[]}}",
                    rc ? "true" : "false");
            }

            return string.Format(CultureInfo.InvariantCulture,
                "{{\"available\":true,\"rcReady\":{0},\"fsActive\":{1},\"hasFrame\":{2}," +
                "\"controlling\":{3},\"missile\":\"{4}\",\"thr\":{5:0.000},\"boost\":{6}," +
                "\"link\":\"{7}\",\"formation\":{8},\"pool\":{9},\"aimX\":{10:0.000},\"aimY\":{11:0.000}," +
                "\"tele\":{12},\"markers\":{13}}}",
                rc ? "true" : "false",
                fsActive ? "true" : "false",
                feed.Active ? "true" : "false",
                controlling ? "true" : "false",
                Escape(rc ? (RcBridge.ControlledMissileName ?? string.Empty) : string.Empty),
                rc ? RcBridge.Throttle01 : 0f,
                rc && RcBridge.BoostActive ? "true" : "false",
                Escape(rc ? (RcBridge.LinkQuality ?? string.Empty) : string.Empty),
                rc && RcBridge.FormationFollowActive ? "true" : "false",
                rc ? StringArray(_cachedPool) : "[]",
                reticle.x, reticle.y,
                tele, markers);
        }

        private static string StringArray(IReadOnlyList<string> items)
        {
            if (items == null || items.Count == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Escape(items[i] ?? string.Empty)).Append('"');
            }
            return sb.Append(']').ToString();
        }

        // Own tiny copy — TelemetryServer.EscapeJson is internal to NOXMFD, not part of the
        // public Api surface, and this extension's JSON needs are small enough not to justify
        // asking for it to be exposed.
        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
