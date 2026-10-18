using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.NPC
{
    public sealed partial class NpcModule : IMultiplayerPresentation
    {
        static Func<string, string, GameObject> _nativePrefab;
        static readonly Dictionary<ulong, float> Shouts = new Dictionary<ulong, float>();
        static float _clientShout;
        void InitNetworkInput()
        { Multiplayer.Message -= NetworkInput; Multiplayer.Message += NetworkInput; Shouts.Clear(); _clientShout = 0; }
        void StopNetworkInput() { Multiplayer.Message -= NetworkInput; Shouts.Clear(); }
        void IMultiplayerPresentation.InitClient()
        {
            Instance = this; ModelDir = Path.Combine(Dir, "Models"); DataDir = Assets.ModuleConfigDir(Id);
            InitNetworkInput();
            Hotkeys.Register(this, ShoutKey, GuestShout, () => GameRefs.InGame && Senses.ModifierHeld());
            Hooks.GetButtonDownUpdate.Prefix(this, Senses.BeforeGetButtonDown); Hooks.GetButtonDo.Prefix(this, Senses.BeforeGetButton); Hooks.GetButtonUpUpdate.Prefix(this, Senses.BeforeGetButtonUp);
            WomenFeature.InitializePresentation(this);
            _nativePrefab = MultiplayerAccess.Call<Func<string, string, GameObject>>(Multiplayer.Type("Net.WorldObjects"), "FindPrefab", typeof(string), typeof(string));
            Multiplayer.PrefabRequested -= NetworkPrefab; Multiplayer.PrefabRequested += NetworkPrefab;
            Multiplayer.AppearanceRequested -= NetworkAppearance; Multiplayer.AppearanceRequested += NetworkAppearance;
        }
        void IMultiplayerPresentation.ShutdownClient()
        {
            StopNetworkInput();
            Multiplayer.PrefabRequested -= NetworkPrefab; Multiplayer.AppearanceRequested -= NetworkAppearance;
            WomenFeature.Shutdown(); _nativePrefab = null; Instance = null;
        }
        static void GuestShout()
        {
            if (!Multiplayer.WorldReady || Time.unscaledTime < _clientShout) return;
            _clientShout = Time.unscaledTime + Mathf.Max(.1f, ShoutCooldown.Value);
            if (GameRefs.Player != null) Senses.ShoutAudio(GameRefs.Player.transform.position, true);
            Multiplayer.Send(90, w => { });
        }
        internal static void RelayShout(ulong peer, Vector3 position)
        {
            if (!Multiplayer.Hosting) return;
            var world = Origin.World(position); Multiplayer.Send(91, w => { w.Write(peer); w.Write(world.x); w.Write(world.y); w.Write(world.z); });
        }
        static void NetworkInput(ulong peer, byte kind, BinaryReader reader)
        {
            if (!Multiplayer.WorldReady) return;
            if (kind == 90 && Multiplayer.Hosting)
            {
                var player = Multiplayer.PlayerFor(peer); float next;
                if (peer == 0 || player == null || !Multiplayer.RemoteTargetable(player) || (Shouts.TryGetValue(peer, out next) && Time.unscaledTime < next)) return;
                Shouts[peer] = Time.unscaledTime + Mathf.Max(.1f, ShoutCooldown.Value);
                Senses.RemoteShout(player); RelayShout(peer, player.transform.position); return;
            }
            if (kind != 91 || peer != 0 || !Multiplayer.Client) return;
            ulong source = reader.ReadUInt64(); var worldPosition = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            if (source != Multiplayer.LocalPeer && !float.IsNaN(worldPosition.sqrMagnitude) && !float.IsInfinity(worldPosition.sqrMagnitude)) Senses.ShoutAudio(Origin.Scene(worldPosition), false);
        }
        static GameObject NetworkPrefab(string name)
        {
            string native = name == "Gungirl" ? "Flexa" : name == "Shoota" ? "Sprokka" : name == "Gungirl_Dead" ? "Flexa_Dead" : name == "Shoota_Dead" ? "Sprokka_Dead" : null;
            return native != null ? _nativePrefab(native, null) : null;
        }
        static void NetworkAppearance(GameObject actor, string identity)
        {
            if (WomenFeature.Active && (identity == "Gungirl" || identity == "Shoota" || identity == "Gungirl_Dead" || identity == "Shoota_Dead"))
                Gungirl.DressCrew(actor, identity, false);
        }
    }
}
