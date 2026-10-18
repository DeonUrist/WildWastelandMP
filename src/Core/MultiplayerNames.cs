using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WildWasteland.Core
{
    internal static class MultiplayerNames
    {
        internal static ConfigEntry<bool> FloatingNames;
        internal sealed class Player { internal TextMesh Tag; internal ulong Peer; internal Color Color; }
        static Func<object, TextMesh> _tag;
        static Func<object, ulong> _peer;
        static Func<ulong, Color> _color;
        static readonly Dictionary<object, Player> Tags = new Dictionary<object, Player>();
        static readonly HashSet<TextMesh> Sleepers = new HashSet<TextMesh>();
        internal static void AddSleeper(TextMesh tag) { Sleepers.Add(tag); }
        internal static void RemoveSleeper(TextMesh tag) { Sleepers.Remove(tag); }
        internal static Dictionary<object, Player>.ValueCollection Players { get { return Tags.Values; } }
        internal static Color ColorOf(ulong peer) { return _color != null ? _color(peer) : Color.white; }
        internal static void Install()
        {
            FloatingNames = Plugin.Settings.Bind("Multiplayer", "FloatingNames", true, "Show floating multiplayer names facing your rendered camera. This is your own HUD preference.");
            var remote = Multiplayer.Type("Game.RemotePlayer");
            _tag = MultiplayerAccess.Get<TextMesh>(remote, "nameTag");
            _peer = MultiplayerAccess.Get<ulong>(remote, "peer");
            _color = MultiplayerAccess.Call<Func<ulong, Color>>(Multiplayer.Type("UI.CompassBar"), "ColorOf", typeof(ulong));
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(remote, "Update"), postfix: AccessTools.Method(typeof(MultiplayerNames), "Updated"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(remote, "Destroy"), prefix: AccessTools.Method(typeof(MultiplayerNames), "Destroyed")))
                throw new InvalidOperationException("Multiplayer floating-name hooks failed");
            Camera.onPreRender += Project;
            Multiplayer.SessionChanged += Changed;
        }
        static void Updated(object __instance)
        {
            var tag = _tag(__instance); if (tag == null) return;
            Player player;
            if (!Tags.TryGetValue(__instance, out player) || player.Tag != tag)
            { ulong peer = _peer(__instance); Tags[__instance] = new Player { Tag = tag, Peer = peer, Color = ColorOf(peer) }; }
            bool visible = FloatingNames.Value && Multiplayer.WorldReady && !LoadingProgress.MenusBlocked;
            if (tag.gameObject.activeSelf != visible) tag.gameObject.SetActive(visible);
        }
        static void Destroyed(object __instance) { Tags.Remove(__instance); }
        static void Changed() { if (!Multiplayer.SessionActive) { Tags.Clear(); Sleepers.Clear(); } }
        static void Project(Camera camera)
        {
            if (camera != GameRefs.MainCamera) return;
            bool visible = Multiplayer.WorldReady && FloatingNames.Value && !LoadingProgress.MenusBlocked;
            foreach (var tag in Sleepers) if (tag != null) tag.gameObject.SetActive(visible);
            if (!visible) return;
            // PreRender follows every PreCull camera override, including WW's virtual third-person view.
            var view = camera.cameraToWorldMatrix;
            var rotation = Quaternion.LookRotation(-view.GetColumn(2), view.GetColumn(1));
            foreach (var player in Tags.Values) if (player.Tag != null && player.Tag.gameObject.activeInHierarchy) player.Tag.transform.rotation = rotation;
            foreach (var tag in Sleepers) if (tag != null) tag.transform.rotation = rotation;
        }
    }
}
