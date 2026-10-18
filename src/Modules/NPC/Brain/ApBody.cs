using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.NPC
{
    // (1.2.0) NPC bodies animated through the player-animation framework (ex Apocaplayer's ModAPI, now Core.Services.Animation provided by
    // the PlayerBody module): the player's own clips and logic - idle, 8 directions x walk / run / sprint, crouch idle and 8 crouched walks
    // for every weapon, the relaxed low-ready walk / run, aiming and firing sets, turns in place, reloads (the whole clip, or one round at a
    // time for the revolver, the shotguns, the bolt rifle and the double barrel), ShotgunPump after each burst of a pump / bolt gun, jumps
    // for the hops, the melee swing, the gun in the right hand at the player's weapon poses and the aim lift.
    //
    // The framework is optional (PlayerBody module off = Services.Animation null): every call into it is in this class; the Npc keeps its
    // character as a plain object. As before the framework is probed only after 3 s (it loads its bundle first) and, until it reports Ready,
    // probed again every few seconds.
    internal static class ApBody
    {
        private static bool _ok, _logged;
        private static float _nextProbe;
        private static IAnimationApi _api;

        internal static bool Available { get { Init(); return _ok; } }
        internal static void Reset() { _ok = false; _api = null; _nextProbe = 0f; _logged = false; }

        private static void Init()
        {
            var api = Services.Animation;
            if (api == null) { if (_ok || _api != null) { _ok = false; _api = null; _logged = false; NpcModule.Log.LogInfo("NPC animations: the player animation framework is off - the game's clips, no sidesteps"); } return; }
            if (_ok && ReferenceEquals(api, _api)) return;
            if (Time.unscaledTime < 3f || Time.unscaledTime < _nextProbe) return;     // let the framework finish loading its bundle
            _nextProbe = Time.unscaledTime + 5f;
            _api = api;
            try { _ok = Probe(api); }
            catch (Exception e) { _ok = false; NpcModule.Warn("NPC animations: the animation framework failed (" + e.GetType().Name + ") - the game's clips"); return; }
            if (_ok) NpcModule.Log.LogInfo("NPC animations: the player animation framework (ModAPI " + api.ApiVersion + ", " + api.ClipNames().Length + " player clips) in use");
            else if (!_logged) { _logged = true; NpcModule.Log.LogInfo("NPC animations: the animation bundle is not ready (yet) - the game's clips"); }
        }
        private static bool Probe(IAnimationApi api) { return api.Ready; }

        // ---------------------------------------------------------------- weapon helpers (no Apocaplayer types)
        // "akm_trash_model" -> "akm_trash", "9mm_borz_smg" -> "borz_smg": the key of the weapon-pose / magazine tables (Apocaplayer.Props.Norm)
        internal static string WeaponKey(Transform weapon)
        {
            if (weapon == null) return null;
            string n = weapon.name.ToLowerInvariant();
            int c = n.IndexOf(" ("); if (c > 0) n = n.Substring(0, c);
            c = n.IndexOf("(clone)"); if (c > 0) n = n.Substring(0, c);
            if (n.EndsWith("_model")) n = n.Substring(0, n.Length - 6);
            if (n.StartsWith("9mm_")) n = n.Substring(4);
            return n.Trim();
        }
        // pistols, revolvers and SMGs are held like pistols (Apocaplayer.Props.KindOf)
        internal static bool IsPistol(string weaponKey)
        {
            string w = weaponKey ?? "";
            return w.Contains("pistol") || w.Contains("revolver") || w.Contains("folk_17") || w.Contains("smg") || w.Contains("borz");
        }

        // ---------------------------------------------------------------- per NPC (only when Available)
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static object Attach(Animator a)
        {
            var api = Services.Animation; if (api == null) return null;
            var c = api.Attach(a);
            if (c == null) return null;
            c.TurnInPlace = true; c.WalkToStop = true;
            if (Core.Multiplayer.Hosting)
                for (var node = a.transform; node != null; node = node.parent)
                    if (FsmUtil.Find(node.gameObject, "Health") != null)
                    { Core.Multiplayer.RegisterRig(node.gameObject, a.GetComponentsInChildren<Transform>(true)); break; }
            return c;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Detach(object body) { var c = body as IAnimatedCharacter; if (c != null) c.Dispose(); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Alive(object body) { var c = body as IAnimatedCharacter; return c != null && c.Valid; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SetWeapon(object body, Transform model, string key) { var c = body as IAnimatedCharacter; if (c != null) c.SetWeapon(model, model != null ? key : null); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Suspend(object body, bool off) { var c = body as IAnimatedCharacter; if (c != null) c.Suspended = off; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Suspended(object body) { var c = body as IAnimatedCharacter; return c == null || c.Suspended; }
        // this frame's situation
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Frame(object body, bool crouched, bool aiming, bool firing, bool airborne, float aimPitch)
        {
            var c = body as IAnimatedCharacter; if (c == null) return;
            c.Crouched = crouched; c.Aiming = aiming; c.Firing = firing; c.Airborne = airborne; c.AimPitch = aimPitch;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static float SpeedCap(object body) { var c = body as IAnimatedCharacter; return c != null ? c.SpeedCap : 0f; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Reload(object body, float seconds, int rounds) { var c = body as IAnimatedCharacter; if (c != null) c.Reload(seconds, rounds); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void CancelReload(object body) { var c = body as IAnimatedCharacter; if (c != null) c.CancelReload(); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Pump(object body) { var c = body as IAnimatedCharacter; if (c != null) c.Pump(); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Jump(object body) { var c = body as IAnimatedCharacter; if (c != null) c.Jump(); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Strike(object body, float seconds) { var c = body as IAnimatedCharacter; if (c != null) c.Strike(seconds); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Describe(object body)
        {
            var c = body as IAnimatedCharacter; if (c == null) return "-";
            return c.WeaponKey + " (" + c.WeaponKind + ") hands " + c.HandsClip + (c.ActionClip != "" ? ", action " + c.ActionClip : "") + (c.UpperClip != "" ? ", upper " + c.UpperClip : "");
        }

        // ---------------------------------------------------------------- the player's weapon facts (only when Available)
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool HasPoses(string key) { var api = Services.Animation; return api != null && api.HasWeaponPoses(key); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool OneRoundAtATime(string key) { var api = Services.Animation; return api != null && api.ReloadsOneRoundAtATime(key); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Cocks(string key) { var api = Services.Animation; return api != null && api.CocksAfterShot(key); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static float ReloadClipSeconds(string key) { var api = Services.Animation; return api != null ? api.ReloadClipSeconds(key) : 0f; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static float RoundSeconds(string key) { var api = Services.Animation; return api != null ? api.RoundSeconds(key) : 0f; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static float NativeSpeed(string tier) { var api = Services.Animation; return api != null ? api.NativeSpeed(tier) : 0f; }
    }
}
