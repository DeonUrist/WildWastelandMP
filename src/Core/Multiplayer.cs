using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WildWasteland.Core
{
    // Networking remains in ApocalypterMP. WW owns world simulation on the host only.
    public static class Multiplayer
    {
        internal const byte PacketType = 250;
        const uint Magic = 0x504D5757;
        internal static Assembly Assembly;
        sealed class BridgeOwner : ModuleBase { public override string Id { get { return "MultiplayerBridge"; } } public override string DisplayName { get { return "Multiplayer bridge"; } } public override void BindConfig(ModuleConfig cfg) { } public override void Init() { } public override void Shutdown() { } }
        internal static readonly IModule PatchOwner = new BridgeOwner();
        static Func<bool> _host, _worldReady, _mainMenu, _chat, _downed;
        static Func<object, int> _mode;
        static Func<object, string> _save;
        static Func<object, ulong> _localPeer;
        static Action<GameObject, byte> _listed;
        static Action<GameObject, bool> _hit;
        static Action<ulong, float> _damage;
        static Action<byte[], bool> _send;
        static Action<ulong, byte[], bool> _sendTo;
        static Action<ulong, byte[]> _handle;
        static Action _stop, _udpJoin;
        static Action<string> _end, _announceClosing;
        static ConfigEntry<string> _udpAddress, _playerName;
        static Func<object, string> _localName;
        static ConfigEntry<int> _udpPort;
        static Func<object, string> _endReason;
        static Action<string, int, bool> _hostWorld;
        static Action<string> _activeSave;
        static Func<object, IDictionary> _objects;
        static Func<object, GameObject> _object;
        static Func<object, int> _id;
        static Func<object, byte> _kind;
        static Func<object, bool> _owned;
        static Action<object, string> _prefab;
        static Func<object, ulong> _owner;
        static Func<Transform, object> _tracked;
        static Func<GameObject, bool> _horn;
        static Action<object> _revoke;
        static Action<object, bool> _claim;
        static Func<object, bool> _carried;
        static Action<object, bool> _visible;
        static Action<object, bool> _hidden;
        static int _off, _joining, _connected;
        static bool _ready, _guestWorld;
        static int _lastMode = -1;
        static bool _lastHosting;
        static readonly List<Remote> Remotes = new List<Remote>();
        static readonly Dictionary<GameObject, Actor> Actors = new Dictionary<GameObject, Actor>();
        static readonly List<GameObject> GoneActors = new List<GameObject>();
        sealed class Actor { internal GameObject Car; internal string Prefab; internal bool Registered; }
        static readonly List<Func<string, GameObject>> PrefabResolvers = new List<Func<string, GameObject>>();
        static Func<string, GameObject>[] _prefabResolvers = new Func<string, GameObject>[0];
        public static event Func<string, GameObject> PrefabRequested
        { add { PrefabResolvers.Add(value); _prefabResolvers = PrefabResolvers.ToArray(); } remove { PrefabResolvers.Remove(value); _prefabResolvers = PrefabResolvers.ToArray(); } }
        public static event Action<GameObject, string> AppearanceRequested;
        internal static event Action<GameObject> CrewPresentationRequested;
        public static event Action<ulong, byte, BinaryReader> Message;
        public static event Action SessionChanged;
        internal sealed class Remote
        {
            internal ulong Peer; internal GameObject Body; internal Transform Root;
        }
        static float _appearanceAt;
        static readonly Dictionary<int, string> Appearances = new Dictionary<int, string>();
        static readonly Dictionary<int, GameObject> AppearanceObjects = new Dictionary<int, GameObject>();
        public static bool Present { get { return _ready; } }
        public static bool Hosting { get { return _ready && _host(); } }
        public static bool Client { get { return _ready && (_guestWorld || _mode(null) == _joining || _mode(null) == _connected); } }
        public static bool Active { get { return Hosting || Client; } }
        public static bool SessionActive { get { return _ready && (_guestWorld || _mode(null) != _off); } }
        public static bool WorldReady { get { return Active && _worldReady(); } }
        public static bool Authority { get { return !Client; } }
        public static bool Typing { get { return _ready && _chat(); } }
        internal static bool LocalTargetable { get { return !_ready || !_downed(); } }
        public static string SaveFile { get { return _ready ? _save(null) : null; } }
        internal static bool InMainMenu { get { return Present ? _mainMenu() : OfflineSaveRuntime.InMainMenu; } }
        internal static bool CanHost { get { return Present && !SessionActive && !MultiplayerHosting.Pending && (_mainMenu() || GameRefs.SaveLoadState == "isPlay" && !SaveBus.Busy) && !LoadingProgress.ScreenVisible; } }
        internal static bool CanJoin { get { return Present && !SessionActive && !MultiplayerHosting.Pending && _mainMenu() && !LoadingProgress.ScreenVisible; } }
        internal static string UdpAddress { get { return Present ? _udpAddress.Value : ""; } }
        internal static string PlayerName { get { return Present ? _localName(null) : ""; } }
        internal static int UdpPort { get { return Present ? _udpPort.Value : 0; } }
        internal static string ConnectionStatus { get { return !Present ? "UNAVAILABLE" : _mode(null) == _joining ? "CONNECTING..." : Hosting ? "HOSTING" : Client ? "CONNECTED" : SessionActive ? "STARTING..." : _endReason(null) ?? "OFFLINE"; } }
        internal static string EndReason { get { return Present ? _endReason(null) : null; } }
        internal static string SetEndpoint(string address, string port, string name)
        {
            int value; System.Net.IPAddress ip;
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 48) return "NAME MUST BE 1-48 CHARACTERS";
            foreach (char ch in name) if (char.IsControl(ch) || ch == '<' || ch == '>') return "NAME CANNOT CONTAIN CONTROL CHARACTERS OR MARKUP";
            if (!int.TryParse(port, out value) || value < 1 || value > 65535) return "PORT MUST BE 1-65535";
            if (!System.Net.IPAddress.TryParse(address.Trim(), out ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return "ENTER A VALID IPV4 ADDRESS";
            if (!Present || SessionActive) return "DISCONNECT BEFORE EDITING";
            _udpAddress.Value = ip.ToString(); _udpPort.Value = value; _playerName.Value = name; return null;
        }
        static void NamedPlayer(ref string __result) { if (!string.IsNullOrWhiteSpace(_playerName.Value)) __result = _playerName.Value; }
        internal static void JoinFromMenu() { if (CanJoin) { _udpJoin(); Refresh(); } }
        internal static void DisconnectFromMenu()
        {
            if (!Present || !SessionActive || SaveBus.Busy || LoadingProgress.ScreenVisible) return;
            if (Hosting) { HostSaveSystem.SaveBeforeExit(CompleteHostDisconnect); return; }
            MultiplayerRecovery.Leave(CompleteDisconnect);
        }
        static void CompleteHostDisconnect() { CompleteDisconnect(); }
        static void CompleteDisconnect() { _end("Disconnected"); Refresh(); }
        public static ulong LocalPeer { get { return _ready ? _localPeer(null) : 0; } }
        internal static int RemoteCount { get { return Hosting ? Remotes.Count : 0; } }
        internal static GameObject RemoteAt(int i) { return Remotes[i].Body; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Init()
        {
            PluginInfo plugin;
            if (!Chainloader.PluginInfos.TryGetValue("ApocalypterMP", out plugin) || ReferenceEquals(plugin.Instance, null)) return;
            Assembly = plugin.Instance.GetType().Assembly;
            MultiplayerLogging.Install();
            try
            {
                var session = Type("Net.NetSession"); var world = Type("Net.WorldObjects");
                MultiplayerTransport.Install();
                var mode = Type("Net.NetSession+Mode");
                _off = Convert.ToInt32(Enum.Parse(mode, "Off")); _joining = Convert.ToInt32(Enum.Parse(mode, "Joining")); _connected = Convert.ToInt32(Enum.Parse(mode, "Connected"));
                _host = MultiplayerAccess.Call<Func<bool>>(session, "get_IsHostingWorld", System.Type.EmptyTypes);
                _mode = MultiplayerAccess.Get<int>(session, "mode");
                _localPeer = MultiplayerAccess.Get<ulong>(session, "LocalId");
                _worldReady = MultiplayerAccess.Call<Func<bool>>(Type("Game.GameLauncher"), "get_IsWorldReady", System.Type.EmptyTypes);
                _mainMenu = MultiplayerAccess.Call<Func<bool>>(Type("Game.GameLauncher"), "get_InMainMenu", System.Type.EmptyTypes);
                _chat = MultiplayerAccess.Call<Func<bool>>(Type("UI.Chat"), "get_Typing", System.Type.EmptyTypes);
                _downed = MultiplayerAccess.Call<Func<bool>>(Type("Game.Survival"), "get_IsDowned", System.Type.EmptyTypes);
                _save = MultiplayerAccess.Get<string>(Type("Game.GameLauncher"), "ActiveSaveFile");
                var kind = Type("Net.WorldObjects+Kind");
                _listed = MultiplayerAccess.Call<Action<GameObject, byte>>(world, "OnObjectListed", typeof(GameObject), kind);
                _hit = MultiplayerAccess.Call<Action<GameObject, bool>>(world, "OnClientHit", typeof(GameObject), typeof(bool));
                _damage = MultiplayerAccess.Call<Action<ulong, float>>(session, "SendPlayerDamage", typeof(ulong), typeof(float));
                _send = MultiplayerAccess.Call<Action<byte[], bool>>(session, "SendToAll", typeof(byte[]), typeof(bool));
                _sendTo = MultiplayerAccess.Call<Action<ulong, byte[], bool>>(session, "SendTo", typeof(ulong), typeof(byte[]), typeof(bool));
                _handle = MultiplayerAccess.Call<Action<ulong, byte[]>>(session, "Handle", typeof(ulong), typeof(byte[]));
                _stop = MultiplayerAccess.Call<Action>(session, "Stop", System.Type.EmptyTypes);
                _udpJoin = MultiplayerAccess.Call<Action>(session, "StartUdpJoin", System.Type.EmptyTypes);
                _end = MultiplayerAccess.Call<Action<string>>(session, "End", typeof(string));
                _announceClosing = MultiplayerAccess.Call<Action<string>>(session, "AnnounceClosing", typeof(string));
                _endReason = MultiplayerAccess.Get<string>(session, "lastEndReason");
                _udpAddress = MultiplayerAccess.Get<ConfigEntry<string>>(Type("Plugin"), "UdpAddress")(null);
                _udpPort = MultiplayerAccess.Get<ConfigEntry<int>>(Type("Plugin"), "UdpPort")(null);
                _playerName = Plugin.Settings.Bind("Multiplayer", "Name", "", "Your multiplayer display name (1-48 characters). Empty uses the Steam or launcher name. Change before hosting or joining.");
                _localName = MultiplayerAccess.Get<string>(session, "LocalName");
                Patch(session, "LocalPlayerName", postfix: "NamedPlayer");
                _hostWorld = MultiplayerAccess.Call<Action<string, int, bool>>(session, "HostWorld", typeof(string), typeof(int), typeof(bool));
                _activeSave = MultiplayerAccess.Call<Action<string>>(Type("Game.GameLauncher"), "set_ActiveSaveFile", typeof(string));
                _objects = MultiplayerAccess.Get<IDictionary>(world, "objects");
                var tracked = Type("Net.WorldObjects+Tracked");
                _object = MultiplayerAccess.Get<GameObject>(tracked, "Object");
                _id = MultiplayerAccess.Get<int>(tracked, "Id");
                _kind = MultiplayerAccess.Get<byte>(tracked, "Kind");
                _owned = MultiplayerAccess.Get<bool>(tracked, "OwnedLocally");
                _prefab = MultiplayerAccess.Set<string>(tracked, "Prefab");
                _owner = MultiplayerAccess.Get<ulong>(tracked, "Owner");
                _tracked = MultiplayerAccess.Call<Func<Transform, object>>(world, "TrackedAncestor", typeof(Transform));
                _horn = MultiplayerAccess.Call<Func<GameObject, bool>>(world, "IsHornPlaying", typeof(GameObject));
                _revoke = MultiplayerAccess.Call<Action<object>>(world, "Revoke", tracked);
                _claim = MultiplayerAccess.Call<Action<object, bool>>(world, "Claim", tracked, typeof(bool));
                _carried = MultiplayerAccess.Get<bool>(tracked, "Carried");
                _visible = MultiplayerAccess.Call<Action<object, bool>>(world, "SetVisible", tracked, typeof(bool));
                _hidden = MultiplayerAccess.Set<bool>(tracked, "Hidden");
                Patch(session, "Update", "BeforeNetwork");
                Patch(session, "Handle", "Receive", parameters: new[] { typeof(ulong), typeof(byte[]) });
                Patch(Type("Game.GameLauncher"), "Launch", "BeforeNetwork");
                Patch(Type("Game.HostSimulation"), "AddPresence", postfix: "PresenceAdded");
                Patch(Type("Game.GroupScaling"), "Wave", "WaveAllowed");
                Patch(world, "HornLureTick", "LuresAllowed");
                Patch(world, "LureCreature", "LuresAllowed");
                Patch(world, "LureTo", "LureAllowed");
                Patch(world, "Register", postfix: "Registered");
                Patch(world, "FindPrefab", "FindPrefab");
                Patch(world, "ApplySpawn", postfix: "SpawnApplied");
                Patch(Type("UI.CompassBar"), "OnGUI", "CompassAllowed");
                Patch(Type("UI.HudMenu"), "OnGUI", "CompassAllowed");
                Patch(Type("UI.HudMenu"), "get_Available", "CompassAvailable");
                Patch(Type("UI.PlayersOverlay"), "Tick", "PlayerOverlayAllowed");
                Patch(Type("Diagnostics.DebugKeys"), "Update", "DebugKeysAllowed");
                Patch(session, "get_Status", "MenuConnectionStatus");
                Patch(Type("Diagnostics.DebugTeleport"), "Update", "DebugAllowed");
                Patch(Type("Diagnostics.DebugSpawner"), "SpawnNext", "DebugAllowed");
                Patch(Type("Diagnostics.LookProbe"), "Log", "DebugAllowed");
                Patch(Type("ModRunner"), "DumpNow", "DebugAllowed");
                MultiplayerParty.Install();
                MultiplayerActors.Install(PatchOwner);
                MultiplayerPlayers.Install();
                MultiplayerNames.Install();
                MultiplayerItems.Install();
                MultiplayerSettings.Install();
                MultiplayerCombat.Install();
                MultiplayerParts.Install();
                MultiplayerReliable.Install();
                MultiplayerSaving.Install();
                MultiplayerHosting.Install();
                MultiplayerNotifications.Install();
                MultiplayerRecovery.Install();
                MultiplayerSleepers.Install();
                MultiplayerRespawn.Install();
                MultiplayerInventory.Install();
                MultiplayerRoster.Install();
                MultiplayerPlayerReset.Install();
                MultiplayerHandshake.Install();
                MultiplayerEffects.Install();
                MultiplayerForces.Install();
                MultiplayerVehicles.Install();
                UltimateToolsCompat.Install();
                MultiplayerHeadless.Install();
                Message += AppearanceMessage;
                _ready = true;
                Log.Always("[Multiplayer] ApocalypterMP bridge ready; WW host owns AI/navigation/events; MP owns transport and replication");
            }
            catch (Exception e)
            {
                _ready = false;
                if (_stop != null) _stop();
                Patches.Unpatch(PatchOwner);
                Hooks.RemoveAll(PatchOwner);
                Scheduler.RemoveAll(PatchOwner); Message = null; SessionChanged = null;
                foreach (string method in new[] { "StartSteamHost", "StartUdpHost", "StartUdpJoin", "HostWorld", "JoinWorld" })
                    Patches.Patch(PatchOwner, AccessTools.Method(Type("Net.NetSession"), method), prefix: AccessTools.Method(typeof(Multiplayer), "BridgeUnavailable"));
                Patches.Patch(PatchOwner, AccessTools.Method(Type("Game.GameLauncher"), "Launch"), prefix: AccessTools.Method(typeof(Multiplayer), "BridgeUnavailable"));
                Log.Error("[Multiplayer] ApocalypterMP API could not be bound; compatibility is unavailable", e);
            }
        }

        internal static Type Type(string suffix) { return Assembly.GetType("ApocalypterMP." + suffix, true); }
        static bool BridgeUnavailable() { return false; }
        static void Patch(Type type, string name, string prefix = null, string postfix = null, Type[] parameters = null)
        {
            if (!Patches.Patch(PatchOwner, AccessTools.Method(type, name, parameters),
                prefix: prefix == null ? null : AccessTools.Method(typeof(Multiplayer), prefix),
                postfix: postfix == null ? null : AccessTools.Method(typeof(Multiplayer), postfix)))
                throw new InvalidOperationException("Could not patch " + type.FullName + "." + name);
        }

        static void BeforeNetwork() { Refresh(); }
        internal static void Tick()
        {
            if (!_ready) return;
            MultiplayerSaving.Tick();
            Refresh();
            for (int i = Remotes.Count - 1; i >= 0; i--) if (Remotes[i].Body == null) Remotes.RemoveAt(i);
            if (Hosting && Actors.Count > 0)
            {
                GoneActors.Clear();
                foreach (var pair in Actors)
                {
                    if (pair.Key == null) { GoneActors.Add(pair.Key); continue; }
                    if (!pair.Value.Registered && (pair.Value.Car == null || ObjectId(pair.Value.Car) != 0))
                    { _listed(pair.Key, 3); pair.Value.Registered = true; }
                }
                foreach (var gone in GoneActors) Actors.Remove(gone);
                if (WorldReady && Time.unscaledTime >= _appearanceAt)
                {
                    _appearanceAt = Time.unscaledTime + 3f;
                    foreach (var pair in Actors) { int id = ObjectId(pair.Key); if (id != 0) { string identity = pair.Value.Prefab; Send(120, w => { w.Write(MultiplayerParts.Epoch); w.Write(id); w.Write(identity); }); } }
                }
            }
            if (Client) ApplyAppearances();
            MultiplayerParty.Tick();
            MultiplayerSettings.Tick();
        }
        static void Refresh()
        {
            if (!_ready) return;
            int mode = _mode(null);
            bool wasGuest = _guestWorld;
            _guestWorld = mode == _joining || mode == _connected || (_guestWorld && !_mainMenu());
            if (_lastMode == mode && wasGuest == _guestWorld && _lastHosting == Hosting) return;
            _lastMode = mode; _lastHosting = Hosting;
            bool client = Client;
            MultiplayerSettings.Changed();
            ModuleHost.MultiplayerChanged();
            if (!Hosting) { Remotes.Clear(); Actors.Clear(); }
            Appearances.Clear(); AppearanceObjects.Clear(); _appearanceAt = 0;
            MultiplayerParty.Reset();
            var changed = SessionChanged; if (changed != null) changed();
            Log.Always("[Multiplayer] role: " + (client ? "guest (host world authority)" : Hosting ? "host (WW world authority)" : "offline"));
        }

        internal static bool HostOnly(IModule module)
        {
            switch (module.Id)
            {
                case "NPC": case "Patrols": case "POI": case "Weather": return true;
                default: return false;
            }
        }
        static bool WaveAllowed(ref IEnumerator __result)
        { if (!_ready) return true; __result = EmptyWave(); return false; }
        static IEnumerator EmptyWave() { yield break; }
        static bool LuresAllowed() { return !_ready; }
        static bool CompassAllowed() { return !_ready; }
        static bool PlayerOverlayAllowed() { return !_ready || !UI.EditorSession.Busy; }
        static bool DebugAllowed() { return !_ready; }
        static bool DebugKeysAllowed()
        {
            return !_ready;
        }
        static bool MenuConnectionStatus(ref string __result)
        {
            if (!_ready || _mode(null) != _off) return true;
            __result = "Net: " + ConnectionStatus + " (MULTIPLAYER in the main / ESC menu)"; return false;
        }
        internal static void HostCurrentWorld(bool steam, string worldName = null)
        {
            if (!Present || SessionActive || InMainMenu || GameRefs.SaveLoadState != "isPlay" || GameRefs.SaveLoadGame == null) return;
            var variables = GameRefs.SaveLoadGame.FsmVariables;
            var file = variables.FindFsmString("SaveFile"); var seed = variables.FindFsmInt("seed");
            string slot = file != null ? file.Value : ES3Settings.defaultSettings.path;
            if (string.IsNullOrEmpty(slot) || seed == null) { Log.Warn("[Multiplayer] Load a world before hosting"); return; }
            slot = System.IO.Path.GetFileName(slot); _activeSave(slot);
            _hostWorld(string.IsNullOrEmpty(worldName) ? System.IO.Path.GetFileNameWithoutExtension(slot) : worldName, seed.Value, steam);
            Refresh();
            if (Hosting) Log.Always("[Multiplayer] Hosted current world " + slot + " over " + (steam ? "Steam" : "UDP"));
        }
        static bool CompassAvailable(ref bool __result)
        { if (!_ready) return true; __result = false; return false; }
        static bool LureAllowed(ref int __result)
        { if (!_ready) return true; __result = 0; return false; }
        static void PresenceAdded(GameObject __0, ulong __1)
        {
            var body = __0 != null ? __0.transform.Find("PlayerPresence") : null;
            if (body == null) return;
            for (int i = 0; i < Remotes.Count; i++) if (Remotes[i].Peer == __1) { Remotes.RemoveAt(i); break; }
            Remotes.Add(new Remote { Peer = __1, Body = body.gameObject, Root = __0.transform });
        }
        public static void RegisterActor(GameObject actor, string prefab, GameObject car)
        {
            if (!Hosting || actor == null) return;
            Actors[actor] = new Actor { Car = car, Prefab = prefab };
            var tracked = _tracked(actor.transform);
            if (tracked != null && _object(tracked) == actor) _prefab(tracked, prefab);
            _appearanceAt = 0;
        }
        static void AppearanceMessage(ulong from, byte kind, BinaryReader reader)
        {
            if (kind != 120 || !Client || from != 0 || reader.ReadUInt64() != MultiplayerParts.Epoch) return;
            int id = reader.ReadInt32(); string identity = reader.ReadString();
            if (identity.Length > 128 || reader.BaseStream.Position != reader.BaseStream.Length || Appearances.Count >= 2048 && !Appearances.ContainsKey(id)) return;
            string old; if (!Appearances.TryGetValue(id, out old) || old != identity) { Appearances[id] = identity; AppearanceObjects.Remove(id); }
        }
        static void ApplyAppearances()
        {
            if (AppearanceRequested == null) return;
            foreach (var pair in Appearances)
            {
                var actor = ObjectById(pair.Key); GameObject applied;
                if (actor == null || AppearanceObjects.TryGetValue(pair.Key, out applied) && applied == actor) continue;
                AppearanceRequested(actor, pair.Value); AppearanceObjects[pair.Key] = actor;
            }
        }
        public static void RegisterRig(GameObject actor, Transform[] transforms, bool seated = false) { if (Hosting) MultiplayerActors.Register(actor, transforms, seated); }
        public static void RefreshRig(GameObject actor) { if (Hosting) MultiplayerActors.Refresh(actor); }
        static void Registered(GameObject __0, object __result)
        { UltimateToolsCompat.Registered(__0); Actor actor; if (_ready && __result != null && __0 != null && Actors.TryGetValue(__0, out actor)) _prefab(__result, actor.Prefab); }
        static bool FindPrefab(string __0, ref GameObject __result)
        {
            if (!_ready) return true;
            foreach (var find in _prefabResolvers)
            { var prefab = find(__0); if (prefab != null) { __result = prefab; return false; } }
            return true;
        }
        static void SpawnApplied(BinaryReader __0)
        {
            if (!Client || AppearanceRequested == null) return;
            long position = __0.BaseStream.Position;
            try
            {
                __0.BaseStream.Position = 1; int id = __0.ReadInt32(); __0.ReadByte(); string prefab = __0.ReadString();
                var actor = ObjectById(id); if (actor != null) AppearanceRequested(actor, prefab);
            }
            finally { __0.BaseStream.Position = position; }
        }
        public static bool IsRemotePlayer(GameObject go)
        {
            if (!Hosting || go == null) return false;
            for (int i = 0; i < Remotes.Count; i++) if (Remotes[i].Root != null && go.transform.IsChildOf(Remotes[i].Root)) return true;
            return false;
        }
        public static GameObject NearestPlayer(Vector3 position, float range = float.MaxValue)
        {
            var local = GameRefs.Player; GameObject best = null; float distance = range * range;
            if (local != null && local.layer != 5) { float d = (local.transform.position - position).sqrMagnitude; if (d <= distance) { best = local; distance = d; } }
            for (int i = 0; i < RemoteCount; i++)
            {
                var remote = Remotes[i].Body; if (remote == null || !remote.activeInHierarchy) continue;
                if (!RemoteTargetable(remote)) continue;
                float d = (remote.transform.position - position).sqrMagnitude;
                if (d < distance) { best = remote; distance = d; }
            }
            return best;
        }
        public static float PlayerDistance(Vector3 position, Transform fallback, bool horizontal = false)
        {
            float result = float.MaxValue;
            if (fallback != null) { var delta = fallback.position - position; if (horizontal) delta.y = 0; result = delta.sqrMagnitude; }
            for (int i = 0; i < RemoteCount; i++)
            {
                var body = Remotes[i].Body; if (body == null || !body.activeInHierarchy) continue;
                var delta = body.transform.position - position; if (horizontal) delta.y = 0;
                result = Mathf.Min(result, delta.sqrMagnitude);
            }
            return Mathf.Sqrt(result);
        }
        public static bool DamageRemote(GameObject target, float damage)
        {
            if (!Hosting || target == null) return false;
            for (int i = 0; i < Remotes.Count; i++)
                if (Remotes[i].Root != null && target.transform.IsChildOf(Remotes[i].Root)) { HostSaveCombat.Activity(); _damage(Remotes[i].Peer, damage); return true; }
            return false;
        }
        public static bool ClientHit(GameObject target)
        { if (!Client || !Creature(target)) return false; _hit(target, false); return true; }
        internal static bool Creature(GameObject target)
        { return CreatureObject(target) != null; }
        internal static GameObject CreatureObject(GameObject target)
        {
            var tracked = Active && target != null ? _tracked(target.transform) : null;
            return tracked != null && _kind(tracked) == 3 ? _object(tracked) : null;
        }
        internal static GameObject TrackedObject(object tracked) { return tracked != null ? _object(tracked) : null; }
        internal static object TrackedState(GameObject root)
        { var state = Active && root != null ? _tracked(root.transform) : null; return state != null && _object(state) == root ? state : null; }
        public static void Register(GameObject go, bool car)
        { if (Hosting && go != null) _listed(go, car ? (byte)1 : (byte)0); }
        public static bool OwnedElsewhere(GameObject go)
        {
            if (!Active || go == null) return false;
            var tracked = _tracked(go.transform);
            return tracked != null && (Client ? !_owned(tracked) : _owner(tracked) != 0);
        }
        internal static bool ItemAuthority(GameObject go)
        { return !Active || (MultiplayerItems.ServerOwned(go) ? Hosting : ObjectId(go) != 0 && !OwnedElsewhere(go)); }
        internal static bool RequestItemAuthority(GameObject go)
        {
            if (ItemAuthority(go)) return true;
            if (MultiplayerItems.ServerOwned(go)) return false;
            var state = TrackedState(go);
            if (Client && state != null && !_carried(state) && GameRefs.Player != null && (go.transform.position - GameRefs.Player.transform.position).sqrMagnitude < 9f) _claim(state, true);
            return false;
        }
        public static int ObjectId(GameObject go)
        {
            if (!Active || go == null) return 0;
            var tracked = _tracked(go.transform);
            return tracked != null && _object(tracked) == go ? _id(tracked) : 0;
        }
        internal static GameObject PlayerFor(ulong peer)
        {
            if (peer == 0) return GameRefs.Player;
            for (int i = 0; i < RemoteCount; i++) if (Remotes[i].Peer == peer && Remotes[i].Body != null && Remotes[i].Body.activeInHierarchy) return Remotes[i].Body;
            return null;
        }
        internal static ulong PeerFor(GameObject player)
        { if (player != null) for (int i = 0; i < RemoteCount; i++) if (Remotes[i].Root != null && player.transform.IsChildOf(Remotes[i].Root)) return Remotes[i].Peer; return 0; }
        public static bool RemoteTargetable(GameObject player) { ulong peer = PeerFor(player); return peer != 0 && MultiplayerPlayers.Targetable(peer); }
        public static GameObject RemoteCar(GameObject player) { return MultiplayerPlayers.Car(PeerFor(player)); }
        public static Vector3 RemoteVelocity(GameObject player) { return MultiplayerPlayers.Velocity(PeerFor(player)); }
        public static bool CarHorn(GameObject car) { return Active && car != null && _horn(car); }
        internal static void PresentCrew(GameObject actor) { var present = CrewPresentationRequested; if (present != null) present(actor); }
        internal static ulong OwnerOf(GameObject go)
        { var tracked = Hosting && go != null ? _tracked(go.transform) : null; return tracked != null ? _owner(tracked) : 0; }
        internal static void ReturnItemToHost(GameObject go, Vector3 position)
        {
            var tracked = Hosting && go != null ? _tracked(go.transform) : null;
            if (tracked == null || _object(tracked) != go || _owner(tracked) == 0) return;
            go.transform.position = position; _hidden(tracked, false); go.SetActive(true); _visible(tracked, true); _revoke(tracked);
        }
        internal static void OwnedItems(ulong peer, List<GameObject> output)
        {
            output.Clear(); if (!Hosting || peer == 0) return;
            foreach (DictionaryEntry entry in _objects(null)) if (_owner(entry.Value) == peer && _object(entry.Value) != null) output.Add(_object(entry.Value));
        }
        public static GameObject ObjectById(int id)
        { var objects = _ready ? _objects(null) : null; return objects != null && objects.Contains(id) ? _object(objects[id]) : null; }

        internal static void Send(byte kind, Action<BinaryWriter> write, ulong? peer = null, bool reliable = true)
        {
            if (!Active) return;
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                writer.Write(PacketType); writer.Write(Magic); writer.Write((byte)1); writer.Write(kind); write(writer);
                var data = stream.ToArray();
                if (data.Length > 1200) throw new InvalidOperationException("WW multiplayer packet exceeds 1200 bytes");
                if (reliable) MultiplayerReliable.Send(data, peer);
                else if (peer.HasValue) _sendTo(peer.Value, data, false); else _send(data, false);
            }
        }
        static bool Receive(ulong __0, byte[] __1)
        {
            if (__1 == null || __1.Length < 1) return true;
            if (__1[0] == MultiplayerReliable.Packet) { if (_ready) MultiplayerReliable.Receive(__0, __1); return false; }
            if (__1[0] != PacketType) return MultiplayerHandshake.BeforeNative(__0, __1) && MultiplayerCombat.BeforeNative(__0, __1) && MultiplayerSleepers.AllowReport(__0, __1);
            if (!_ready || __1.Length < 7 || __1.Length > 1200 || (!Hosting && __0 != 0)) return false;
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(__1, false)))
                {
                    reader.ReadByte(); if (reader.ReadUInt32() != Magic || reader.ReadByte() != 1) return false;
                    byte kind = reader.ReadByte();
                    if (kind < 16) MultiplayerParty.Receive(__0, kind, reader);
                    else { var handler = Message; if (handler != null) handler(__0, kind, reader); }
                }
            }
            catch (Exception e) { Log.Warn("[Multiplayer] Rejected WW packet: " + e.Message); }
            return false;
        }
        internal static void RawTo(ulong peer, byte[] data) { _sendTo(peer, data, true); }
        internal static void Deliver(ulong peer, byte[] data) { _handle(peer, data); }
    }

    internal interface IMultiplayerPresentation
    {
        void InitClient();
        void ShutdownClient();
    }
}
