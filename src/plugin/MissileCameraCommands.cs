using System;
using UnityEngine;

namespace RcMissileCamera
{
    // Own envelope, own shape — NOXMFD's CommandEnvelope is never touched for this
    // (docs/extensions-api.md surface #3: every extension owns both the client JS that builds
    // its POST body and the C# that parses it, no shared schema needed). Flat fields, same
    // JsonUtility-flakiness-with-nested-objects reasoning NOXMFD's own envelope follows.
    [Serializable]
    internal class MissileCameraCommandEnvelope
    {
        public string cmd = string.Empty;
        public float  x;      // aim: yaw delta, degrees (right positive)
        public float  y;      // aim: pitch delta, degrees (up negative)
        public float  v;      // throttle-set: absolute 0..1 · throttle-adjust: relative delta
        public bool   on;     // boost hold state
        public int    index;  // take-at: pool index
    }

    // Registered as the Api.CommandHandler for this extension (Plugin.cs) — invoked on the
    // Unity main thread once per queued POST to /ext/rc-missile-camera/command.
    internal static class MissileCameraCommands
    {
        internal static void Handle(string json)
        {
            MissileCameraCommandEnvelope? env;
            try { env = JsonUtility.FromJson<MissileCameraCommandEnvelope>(json); }
            catch (Exception ex) { Plugin.Log?.LogDebug($"[MISSILE CAMERA] malformed command: {ex.Message}"); return; }
            if (env == null || string.IsNullOrEmpty(env.cmd)) return;

            switch (env.cmd)
            {
                case "aim":             RcBridge.InjectAimDelta(env.x, env.y); break;
                case "throttle-set":    RcBridge.SetThrottle01(env.v); break;
                case "throttle-adjust": RcBridge.AdjustThrottle(env.v); break;
                case "boost":           RcBridge.SetBoostHeld(env.on); break;
                case "take":            RcBridge.TakeNearest(); break;
                case "take-at":         RcBridge.RefreshPool(); RcBridge.TakeAt(env.index); break;
                case "release":         RcBridge.Release(); break;
                case "formation":       RcBridge.ToggleFormationFollow(); break;
                case "detonate":        RcBridge.ManualDetonate(); break;
                case "refresh-pool":    RcBridge.RefreshPool(); break;
                case "vision-cycle":    McBridge.CycleVisionMode(); break;
                default: Plugin.Log?.LogDebug($"[MISSILE CAMERA] unknown command '{env.cmd}'."); break;
            }
        }
    }
}
