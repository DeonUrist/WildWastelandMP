using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using UnityEngine.UI;
using WildWasteland.Core.UI;

namespace WildWasteland.Core
{
    // One explicit object anchor per player; sleeping and sleep votes do not select it.
    internal static class MultiplayerRespawn
    {
        const string SelectedKey = "ww.spawnSet";
        const byte CampPacket = 163, AnchorPacket = 164;
        const int SurfaceMask = (1 << 0) | (1 << 8) | (1 << 14) | (1 << 16);
        sealed class BedState { internal bool Has; internal Vector3 Position, Local; internal int Item; }
        static Func<object, bool> _hasBed;
        static Action<object, bool> _setHasBed, _teleporting, _frozen;
        static Func<object, bool> _isFrozen;
        static Func<object, Vector3> _bed, _bedLocal;
        static Action<object, Vector3> _setBed, _setBedLocal;
        static Func<object, int> _bedItem;
        static Action<object, int> _setBedItem;
        static Action<object, float> _downedUntil;
        static Func<object, IDictionary> _objects, _records;
        static Func<object, GameObject> _object;
        static Func<Transform, object> _tracked;
        static Func<ulong, object> _record;
        static Func<object, Dictionary<string, float>> _stats;
        static Action _report;
        static Action<string> _notice;
        delegate bool HostPosition(out Vector3 position);
        static HostPosition _hostPosition;
        static ConfigEntry<bool> _downedEnabled;
        static readonly Dictionary<int, GameObject> Anchors = new Dictionary<int, GameObject>();
        static readonly HashSet<GameObject> Protected = new HashSet<GameObject>();
        static readonly Collider[] Capsule = new Collider[32];
        static readonly RaycastHit[] SurfaceHits = new RaycastHit[32];
        static Text _hint;
        static string _hintText;
        static Vector3 _camp;
        static bool _hasCamp, _selected, _moving;
        static float _nextCache, _nextCamp, _nextHint, _nextSelect;
        static int _lookFrame = -1;
        static GameObject _looked;
        static int _networkAnchor, _anchorNumber;
        static ulong _epoch;
        internal static bool Resetting { get; private set; }
        internal static bool ResetSucceeded { get; private set; }
        internal static bool Placing { get { return _moving; } }
        static Func<GameObject, IEnumerator> _leaveCar;
        static Action<GameObject> _kickOut;
        internal static void Install()
        {
            var survival = Multiplayer.Type("Game.Survival"); var world = Multiplayer.Type("Net.WorldObjects"); var session = Multiplayer.Type("Net.NetSession");
            _hostPosition = MultiplayerAccess.Call<HostPosition>(session, "TryGetHostPosition", typeof(Vector3).MakeByRefType());
            _leaveCar = MultiplayerAccess.Call<Func<GameObject, IEnumerator>>(survival, "LeaveCar", typeof(GameObject));
            _kickOut = MultiplayerAccess.Call<Action<GameObject>>(world, "KickOutOf", typeof(GameObject));
            _hasBed = MultiplayerAccess.Get<bool>(survival, "HasBed"); _setHasBed = MultiplayerAccess.Set<bool>(survival, "HasBed");
            _bed = MultiplayerAccess.Get<Vector3>(survival, "Bed"); _setBed = MultiplayerAccess.Set<Vector3>(survival, "Bed");
            _bedLocal = MultiplayerAccess.Get<Vector3>(survival, "BedLocal"); _setBedLocal = MultiplayerAccess.Set<Vector3>(survival, "BedLocal");
            _bedItem = MultiplayerAccess.Get<int>(survival, "BedItem"); _setBedItem = MultiplayerAccess.Set<int>(survival, "BedItem");
            _downedUntil = MultiplayerAccess.Set<float>(survival, "downedUntil");
            _teleporting = MultiplayerAccess.Set<bool>(Multiplayer.Type("Game.SafeTeleport"), "teleporting");
            _frozen = MultiplayerAccess.Set<bool>(Multiplayer.Type("Game.PauseControl"), "PlayerFrozen");
            _isFrozen = MultiplayerAccess.Get<bool>(Multiplayer.Type("Game.PauseControl"), "PlayerFrozen");
            _objects = MultiplayerAccess.Get<IDictionary>(world, "objects"); _object = MultiplayerAccess.Get<GameObject>(Multiplayer.Type("Net.WorldObjects+Tracked"), "Object");
            _tracked = MultiplayerAccess.Call<Func<Transform, object>>(world, "TrackedAncestor", typeof(Transform));
            _records = MultiplayerAccess.Get<IDictionary>(Multiplayer.Type("Worlds.PlayerRegistry"), "records");
            _record = MultiplayerAccess.Call<Func<ulong, object>>(session, "RecordOf", typeof(ulong));
            _stats = MultiplayerAccess.Get<Dictionary<string, float>>(Multiplayer.Type("Worlds.PlayerRecord"), "Stats");
            _report = MultiplayerAccess.Call<Action>(session, "SendPlayerSave", Type.EmptyTypes);
            _notice = MultiplayerAccess.Call<Action<string>>(Multiplayer.Type("UI.Chat"), "Notice", typeof(string));
            _downedEnabled = MultiplayerAccess.Get<ConfigEntry<bool>>(Multiplayer.Type("Plugin"), "DownedEnabled")(null);
            Patch(survival, "EnterDowned", after: "NoDeadline");
            Patch(survival, "CanBeDowned", before: "CanWait");
            Patch(survival, "OnBedConfirmed", "BeforeSleep", "AfterSleep");
            Patch(Multiplayer.Type("Game.PlayerVitals"), "Capture", after: "Capture");
            Patch(Multiplayer.Type("Game.PlayerVitals"), "ApplyBed", after: "Restored");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(survival, "OnGUI"), transpiler: AccessTools.Method(typeof(MultiplayerRespawn), "NoCountdown")))
                throw new InvalidOperationException("MP revive countdown presentation hook failed");
            Type routine = null;
            foreach (var nested in survival.GetNestedTypes(BindingFlags.NonPublic)) if (nested.Name.StartsWith("<DieAndRespawn>", StringComparison.Ordinal)) { routine = nested; break; }
            if (routine == null || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(routine, "MoveNext"), transpiler: AccessTools.Method(typeof(MultiplayerRespawn), "RespawnTarget"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(session, "TryRestorePlayer"), transpiler: AccessTools.Method(typeof(MultiplayerRespawn), "ReconnectTarget"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(session, "TrySpawnNextToHost"), transpiler: AccessTools.Method(typeof(MultiplayerRespawn), "ReconnectTarget")))
                throw new InvalidOperationException("MP safe respawn/reconnect placement hooks failed");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(typeof(GetDistance), "DoGetDistance"), prefix: AccessTools.Method(typeof(MultiplayerRespawn), "RetentionDistance"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(typeof(DestroyObject), "OnEnter"), prefix: AccessTools.Method(typeof(MultiplayerRespawn), "RetentionDestroy")))
                throw new InvalidOperationException("MP respawn anchor retention hooks failed");
            Multiplayer.SessionChanged += Changed; Multiplayer.Message += Receive;
            Hooks.GetButtonDownUpdate.Prefix(Multiplayer.PatchOwner, SecondaryUse);
            Scheduler.Interval(Multiplayer.PatchOwner, "Respawn anchors and camp", 1f, Tick);
            Scheduler.EveryFrame(Multiplayer.PatchOwner, "Respawn anchor selection", Interaction, Phase.LateUpdateLast);
        }
        static void Patch(Type type, string method, string before = null, string after = null)
        {
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(type, method), prefix: before == null ? null : AccessTools.Method(typeof(MultiplayerRespawn), before), postfix: after == null ? null : AccessTools.Method(typeof(MultiplayerRespawn), after)))
                throw new InvalidOperationException("MP respawn hook failed: " + method);
        }
        static void Changed()
        {
            if (Multiplayer.SessionActive && _epoch == MultiplayerParts.Epoch) return;
            _epoch = MultiplayerParts.Epoch; Anchors.Clear(); Protected.Clear(); _selected = _hasCamp = false; _networkAnchor = _anchorNumber = 0; _nextCache = _nextCamp = 0; Hint(null);
        }
        static void NoDeadline() { if (Multiplayer.Active) _downedUntil(null, float.PositiveInfinity); }
        static bool CanWait(ref bool __result) { if (!Multiplayer.Active) return true; __result = _downedEnabled.Value; return false; }
        static void BeforeSleep(out BedState __state)
        { __state = new BedState { Has = _hasBed(null), Position = _bed(null), Item = _bedItem(null), Local = _bedLocal(null) }; }
        static void AfterSleep(BedState __state)
        { _setHasBed(null, __state.Has); _setBed(null, __state.Position); _setBedItem(null, __state.Item); _setBedLocal(null, __state.Local); }
        static IEnumerable<CodeInstruction> NoCountdown(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var code in instructions)
            {
                if (code.opcode == OpCodes.Ldstr && (string)code.operand == "A teammate can revive you: {0:F0} s")
                { code.operand = "A teammate can revive you. No time limit."; count++; }
                yield return code;
            }
            if (count != 1) throw new InvalidOperationException("Unexpected MP downed countdown labels: " + count);
        }
        static IEnumerable<CodeInstruction> RespawnTarget(IEnumerable<CodeInstruction> instructions)
        { return ReplaceTeleport(instructions, "Respawn", true); }
        static IEnumerable<CodeInstruction> ReconnectTarget(IEnumerable<CodeInstruction> instructions)
        { return ReplaceTeleport(instructions, "Reconnect", false); }
        static IEnumerable<CodeInstruction> ReplaceTeleport(IEnumerable<CodeInstruction> instructions, string replacement, bool notices)
        {
            var target = AccessTools.Method(Multiplayer.Type("Game.SafeTeleport"), "To"); int count = 0;
            foreach (var code in instructions)
            {
                if (code.Calls(target)) { code.operand = AccessTools.Method(typeof(MultiplayerRespawn), replacement); count++; }
                if (notices && code.opcode == OpCodes.Ldstr)
                {
                    if ((string)code.operand == "You woke up in the last bed you slept in." || (string)code.operand == "You have no bed yet: sleep in one to respawn there.") code.operand = "";
                }
                yield return code;
            }
            if (count != 1) throw new InvalidOperationException("Unexpected MP placement calls: " + replacement + " " + count);
        }
        static void Capture(Dictionary<string, float> __result)
        {
            if (!Multiplayer.Active || __result == null) return;
            __result[SelectedKey] = _selected ? 1 : 0;
            if (_hasCamp) { __result["ww.camp.x"] = _camp.x; __result["ww.camp.y"] = _camp.y; __result["ww.camp.z"] = _camp.z; }
        }
        static void Restored(Dictionary<string, float> __0)
        {
            if (!Multiplayer.Active || __0 == null) return;
            float value; _selected = __0.TryGetValue(SelectedKey, out value) && value == 1; _setHasBed(null, _selected);
            float x, y, z;
            if (!_hasCamp && __0.TryGetValue("ww.camp.x", out x) && __0.TryGetValue("ww.camp.y", out y) && __0.TryGetValue("ww.camp.z", out z))
            { _camp = new Vector3(x, y, z); _hasCamp = Finite(_camp); }
            _nextCache = 0;
        }
        static bool Number(GameObject go, out int number)
        { number = 0; if (go == null) return false; string name = go.name; int at = name.LastIndexOf("(Clone)", StringComparison.Ordinal); return at >= 0 && int.TryParse(name.Substring(at + 7), out number) && number != 0; }
        static bool IsAnchor(GameObject go)
        {
            if (go == null || !go.activeInHierarchy) return false;
            string name = go.name;
            return name.IndexOf("bed", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("sleep", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("seat", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        internal static bool ProtectedObject(GameObject go)
        { if (go == null) return false; for (var t = go.transform; t != null; t = t.parent) if (Protected.Contains(t.gameObject)) return true; return false; }
        static void Protect(GameObject go)
        {
            if (go == null) return;
            Protected.Add(go);
            var vehicle = go.GetComponentInParent<NWH.VehiclePhysics2.VehicleController>();
            if (vehicle != null) Protected.Add(vehicle.gameObject);
        }
        static bool RetentionDistance(GetDistance __instance)
        {
            if (!Multiplayer.Active || __instance.storeResult == null || __instance.Fsm == null) return true;
            string fsm = __instance.Fsm.Name;
            if (fsm != "DeleteFarAwayItems" && fsm != "DeactivateFarAwayItems" && fsm != "DistanceKinematic") return true;
            var target = __instance.target != null ? __instance.target.Value : null;
            var owner = __instance.Fsm.GetOwnerDefaultTarget(__instance.gameObject);
            if (!ProtectedObject(target) && !ProtectedObject(owner)) return true;
            __instance.storeResult.Value = 0; return false;
        }
        static bool RetentionDestroy(DestroyObject __instance)
        {
            if (!Multiplayer.Active || __instance.Fsm == null) return true;
            string fsm = __instance.Fsm.Name;
            if (fsm != "DeleteFarAwayItems" && fsm != "DeleteFallingItems") return true;
            if (!ProtectedObject(__instance.gameObject != null ? __instance.gameObject.Value : null)) return true;
            __instance.Finish(); return false;
        }
        static void Tick()
        {
            if (!Multiplayer.WorldReady || MultiplayerSaving.Frozen || SaveBus.Busy) return;
            if (_epoch != MultiplayerParts.Epoch) { _epoch = MultiplayerParts.Epoch; _nextCache = 0; }
            if (!_hasCamp && Multiplayer.Hosting)
            {
                var starter = GameObject.Find("Starter_Area"); var point = starter != null ? starter.transform.Find("playerPos") : null;
                if (point != null) { _camp = Origin.World(point.position); _hasCamp = true; }
            }
            RefreshAnchors();
            if (_hasCamp && Multiplayer.Hosting && Time.unscaledTime >= _nextCamp)
            {
                _nextCamp = Time.unscaledTime + 3f; Multiplayer.Send(CampPacket, w => { w.Write(MultiplayerParts.Epoch); Write(w, _camp); });
                foreach (ulong peer in MultiplayerReliable.ConnectedPeers())
                {
                    var record = _record(peer); if (record == null) continue;
                    float selected, number; var stats = _stats(record); GameObject anchor;
                    if (stats.TryGetValue(SelectedKey, out selected) && selected == 1 && stats.TryGetValue("b.item", out number) && Anchors.TryGetValue(Mathf.RoundToInt(number), out anchor)) SendAnchor(peer, Mathf.RoundToInt(number), anchor, false);
                }
            }
        }
        static void RefreshAnchors()
        {
            if (Time.unscaledTime < _nextCache) return; _nextCache = Time.unscaledTime + 1f;
            Anchors.Clear(); Protected.Clear();
            foreach (DictionaryEntry entry in _objects(null))
            { var go = _object(entry.Value); int number; if (go != null && IsAnchor(go) && Number(go, out number)) Anchors[number] = go; }
            // Guest replicas are named MP_<prefab>, without native (Clone)<item-number> suffixes.
            var network = Multiplayer.Client && _networkAnchor != 0 ? Multiplayer.ObjectById(_networkAnchor) : null;
            if (network != null && IsAnchor(network) && _anchorNumber == _bedItem(null)) Anchors[_anchorNumber] = network;
            GameObject selected;
            if (_selected && Anchors.TryGetValue(_bedItem(null), out selected)) { Protect(selected); _setBed(null, Origin.World(selected.transform.position)); }
            if (Multiplayer.Hosting) foreach (DictionaryEntry entry in _records(null))
            {
                if ((string)entry.Key == "host") continue;
                var stats = _stats(entry.Value); float enabled, item;
                if (!stats.TryGetValue(SelectedKey, out enabled) || enabled != 1 || !stats.TryGetValue("b.item", out item) || !Anchors.TryGetValue(Mathf.RoundToInt(item), out selected)) continue;
                Protect(selected); var p = Origin.World(selected.transform.position); stats["b.x"] = p.x; stats["b.y"] = p.y; stats["b.z"] = p.z;
            }
        }
        static void Interaction()
        {
            if (!Multiplayer.WorldReady || !GameRefs.InGame || !Multiplayer.LocalTargetable || Multiplayer.Typing || EditorSession.Busy || ApocasetterBridge.InputBlocked || !Application.isFocused || MultiplayerSaving.Frozen || _moving)
            { Hint(null); return; }
            var anchor = LookedAnchor();
            if (anchor == null) { Hint(null); return; }
            Hint("Set respawn point: Secondary Use");
            if (!Input.GetButtonDown("Use_2") || Time.unscaledTime < _nextSelect) return;
            _nextSelect = Time.unscaledTime + .5f;
            if (Multiplayer.Hosting) Select(0, anchor);
            else { int id = Multiplayer.ObjectId(anchor); Multiplayer.Send(AnchorPacket, w => { w.Write(MultiplayerParts.Epoch); w.Write((byte)0); w.Write(id); }); }
        }
        static GameObject LookedAnchor()
        {
            if (_lookFrame == Time.frameCount) return _looked;
            _lookFrame = Time.frameCount; _looked = null;
            var camera = GameRefs.MainCamera; var player = GameRefs.Player; if (camera == null || player == null) return null;
            RaycastHit hit; var ray = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            if (!Physics.Raycast(ray, out hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || (hit.point - player.transform.position).sqrMagnitude > 16f) return null;
            var tracked = _tracked(hit.collider.transform); var anchor = tracked != null ? _object(tracked) : null; int number;
            if (IsAnchor(anchor) && (Multiplayer.Client || Number(anchor, out number))) _looked = anchor;
            return _looked;
        }
        static bool SecondaryUse(GetButtonDown action)
        {
            if (!Multiplayer.WorldReady || !GameRefs.InGame || !Multiplayer.LocalTargetable || Multiplayer.Typing || EditorSession.Busy || ApocasetterBridge.InputBlocked || !Application.isFocused || _moving || MultiplayerSaving.Frozen
                || action.buttonName == null || action.buttonName.Value != "Use_2" || !Input.GetButtonDown("Use_2") || LookedAnchor() == null) return true;
            // Selecting a bed/seat must not also detach it through a native secondary-use action.
            if (action.storeResult != null) action.storeResult.Value = false;
            return false;
        }
        static void Hint(string text)
        {
            if (_hint == null && Time.unscaledTime >= _nextHint)
            { _nextHint = Time.unscaledTime + 2f; var root = GameObject.Find("Canvas"); var t = root != null ? root.transform.Find("ItemGrab") : null; _hint = t != null ? t.GetComponent<Text>() : null; }
            if (_hint != null && (text != null || _hint.text == _hintText)) _hint.text = text ?? "";
            _hintText = text;
        }
        static void Select(ulong peer, GameObject anchor)
        {
            var player = Multiplayer.PlayerFor(peer); int number;
            if (!IsAnchor(anchor) || player == null || !Number(anchor, out number) || (player.transform.position - anchor.transform.position).sqrMagnitude > 25f || MultiplayerSaving.Frozen || SaveBus.Busy) return;
            var position = Origin.World(anchor.transform.position);
            if (peer == 0) { ApplySelection(number, position); _notice("Respawn point set. Previous respawn point replaced."); }
            else
            {
                var record = _record(peer); if (record == null || !MultiplayerPlayers.Targetable(peer)) return;
                var stats = _stats(record); stats[SelectedKey] = 1; stats["b.item"] = number;
                stats["b.x"] = position.x; stats["b.y"] = position.y; stats["b.z"] = position.z;
                stats["b.lx"] = stats["b.ly"] = stats["b.lz"] = 0;
                SendAnchor(peer, number, anchor, true);
            }
            _nextCache = 0; RefreshAnchors(); MultiplayerRecovery.Checkpoint();
        }
        static void ApplySelection(int number, Vector3 position)
        { _selected = true; _setHasBed(null, true); _setBedItem(null, number); _setBed(null, position); _setBedLocal(null, Vector3.zero); _nextCache = 0; }
        static void SendAnchor(ulong peer, int number, GameObject anchor, bool confirmed)
        {
            int id = Multiplayer.ObjectId(anchor); var point = Origin.World(anchor.transform.position);
            Multiplayer.Send(AnchorPacket, w => { w.Write(MultiplayerParts.Epoch); w.Write(confirmed ? (byte)1 : (byte)2); w.Write(number); w.Write(id); Write(w, point); }, peer);
        }
        static bool Finite(Vector3 v) { return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z); }
        static void Write(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        static Vector3 Read(BinaryReader r) { var v = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); if (!Finite(v)) throw new InvalidDataException("Invalid respawn position"); return v; }
        static void Receive(ulong from, byte kind, BinaryReader r)
        {
            if (kind != CampPacket && kind != AnchorPacket) return;
            if (r.ReadUInt64() != MultiplayerParts.Epoch || MultiplayerParts.Epoch == 0) return;
            if (kind == CampPacket)
            { var position = Read(r); if (Multiplayer.Client && from == 0 && r.BaseStream.Position == r.BaseStream.Length) { _camp = position; _hasCamp = true; } return; }
            byte mode = r.ReadByte(); int id = r.ReadInt32();
            if (mode == 0)
            { if (Multiplayer.Hosting && from != 0 && r.BaseStream.Position == r.BaseStream.Length) Select(from, Multiplayer.ObjectById(id)); return; }
            int network = r.ReadInt32(); var point = Read(r);
            if (mode > 2 || !Multiplayer.Client || from != 0 || r.BaseStream.Position != r.BaseStream.Length) return;
            if (mode == 1) { ApplySelection(id, point); _report(); _notice("Respawn point set. Previous respawn point replaced."); }
            if (_selected && _bedItem(null) == id) { _anchorNumber = id; _networkAnchor = network; _nextCache = 0; }
        }
        static IEnumerator Respawn(Vector3 ignored) { return Place(Vector3.zero, true); }
        internal static bool StartingPosition(out Vector3 position) { position = _camp; return _hasCamp; }
        internal static bool BesideHost(out Vector3 position)
        {
            position = Vector3.zero;
            if (Multiplayer.Hosting) { if (GameRefs.Player == null) return false; position = Origin.World(GameRefs.Player.transform.position); }
            else if (!Multiplayer.Client || !_hostPosition(out position)) return false;
            position += new Vector3(2f, 0f, 2f); return Finite(position);
        }
        internal static IEnumerator ResetToCamp(Vector3 position)
        {
            ResetSucceeded = false;
            if (_moving || Resetting || !Multiplayer.WorldReady || GameRefs.Player == null) yield break;
            var player = GameRefs.Player; ulong epoch = MultiplayerParts.Epoch; int generation = SaveBus.Generation;
            var vehicle = player.GetComponentInParent<NWH.VehiclePhysics2.VehicleController>();
            bool frozen = _isFrozen(null); Resetting = true; _frozen(null, true);
            try
            {
                yield return _leaveCar(player);
                if (Multiplayer.WorldReady && player != null && GameRefs.Player == player && epoch == MultiplayerParts.Epoch && generation == SaveBus.Generation)
                {
                    if (vehicle != null && player.transform.IsChildOf(vehicle.transform)) _kickOut(vehicle.gameObject);
                    yield return Place(position, false, false, true);
                }
            }
            finally { _frozen(null, frozen); Resetting = false; }
        }
        static IEnumerator Reconnect(Vector3 position) { return Place(position, false, nearHost: !MultiplayerSleepers.RestoringSleeper); }
        internal static IEnumerator FinishWake(Vector3 position) { return Place(position, false, true); }
        static IEnumerator Place(Vector3 saved, bool respawn, bool handoff = false, bool reset = false, bool nearHost = false)
        {
            var player = GameRefs.Player; var body = player != null ? player.GetComponent<Rigidbody>() : null;
            if (body == null) yield break;
            bool wasKinematic = body.isKinematic, wasFrozen = _isFrozen(null); _moving = true; body.isKinematic = true; _teleporting(null, true); _frozen(null, true);
            ulong resetEpoch = MultiplayerParts.Epoch; int resetGeneration = SaveBus.Generation;
            float fallbackAt = Time.unscaledTime + 30f; bool campOnly = false;
            try
            {
                while (Multiplayer.Active && Multiplayer.WorldReady && player != null && body != null
                    && (!reset || resetEpoch == MultiplayerParts.Epoch && resetGeneration == SaveBus.Generation))
                {
                    if (MultiplayerSaving.Frozen || SaveBus.Busy) { yield return new WaitForSecondsRealtime(.25f); continue; }
                    RefreshAnchors(); Vector3 target = saved; GameObject anchor = null;
                    if (respawn)
                    {
                        if (!campOnly && _selected && Anchors.TryGetValue(_bedItem(null), out anchor)) target = Origin.World(anchor.transform.position);
                        else if (!campOnly && _selected && Time.unscaledTime < fallbackAt) target = _bed(null);
                        else
                        {
                            if (Multiplayer.Client ? !BesideHost(out target) : !StartingPosition(out target)) { yield return new WaitForSecondsRealtime(.25f); continue; }
                            campOnly = true;
                        }
                    }
                    else if (nearHost && !BesideHost(out target)) { yield return new WaitForSecondsRealtime(.25f); continue; }
                    player.transform.position = Origin.Scene(target) + Vector3.up * 3f;
                    Physics.SyncTransforms();
                    Vector3 place;
                    if ((!respawn || anchor != null || campOnly) && Surface(Origin.Scene(target), player.transform, out place))
                    {
                        player.transform.position = place; VehicleLoadPlacement.Stand(player); body.isKinematic = false; body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.isKinematic = wasKinematic;
                        Physics.SyncTransforms();
                        if (respawn) _notice(campOnly ? Multiplayer.Client ? "You respawned beside the host." : "You respawned at the initial camp." : "You respawned beside your selected spawn object.");
                        Log.Always(nearHost || respawn && campOnly && Multiplayer.Client ? "[Multiplayer] Player placed beside host" : respawn ? campOnly ? "[Multiplayer] Respawn placed at initial camp" : "[Multiplayer] Respawn placed beside selected object " + _bedItem(null) : "[Multiplayer] Player restored on a clear surface beside the saved position");
                        if (reset) ResetSucceeded = true;
                        else if (!respawn)
                        {
                            if (handoff)
                            {
                                body.isKinematic = true;
                                MultiplayerSleepers.WakePlaced(Origin.World(place));
                                while (Multiplayer.Client && MultiplayerSleepers.AwaitingWake) yield return null;
                            }
                            else MultiplayerSleepers.InitialPlaced();
                        }
                        yield break;
                    }
                    if (respawn && Time.unscaledTime >= fallbackAt) campOnly = true;
                    yield return new WaitForSecondsRealtime(.25f);
                }
            }
            finally { if (body != null) body.isKinematic = wasKinematic; _teleporting(null, false); _frozen(null, wasFrozen); _moving = false; }
        }
        internal static bool Surface(Vector3 center, Transform player, out Vector3 position)
        {
            // Main terrain must have replaced the draft before any object/floor is used as support.
            RaycastHit terrain;
            if (!Physics.Raycast(center + Vector3.up * 1500f, Vector3.down, out terrain, 3000f, 1 << 14, QueryTriggerInteraction.Ignore) || terrain.collider.name == "Draft Terrain")
            { position = Vector3.zero; return false; }
            var tile = terrain.collider.GetComponentInParent<MapMagic.Terrains.TerrainTile>();
            if (!(terrain.collider is TerrainCollider) || !terrain.collider.enabled || !terrain.collider.gameObject.activeInHierarchy
                || tile != null && (tile.main == null || !tile.main.applyReady || tile.main.terrain == null || terrain.collider.gameObject != tile.main.terrain.gameObject))
            { position = Vector3.zero; return false; }
            for (int ring = 0; ring < 5; ring++) for (int step = 0; step < 12; step++)
            {
                float angle = step * Mathf.PI / 6f, radius = ring == 0 ? 0 : ring * 1.5f;
                var probe = center + new Vector3(Mathf.Cos(angle) * radius, 4f, Mathf.Sin(angle) * radius);
                int count = Physics.RaycastNonAlloc(probe, Vector3.down, SurfaceHits, 40f, SurfaceMask, QueryTriggerInteraction.Ignore);
                if (count >= SurfaceHits.Length) continue;
                for (int i = 0; i < count; i++)
                {
                    var hit = SurfaceHits[i]; if (hit.normal.y < .7f || hit.collider.name == "Draft Terrain" || hit.collider.transform.IsChildOf(player) || hit.point.y < terrain.point.y - .5f) continue;
                    var feet = hit.point + Vector3.up * .08f;
                    int blocked = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * .35f, feet + Vector3.up * 1.55f, .32f, Capsule, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    bool free = blocked < Capsule.Length;
                    for (int j = 0; j < blocked && j < Capsule.Length; j++) if (!Capsule[j].transform.IsChildOf(player)) { free = false; break; }
                    if (!free) continue;
                    position = hit.point + Vector3.up * 1.05f; return true;
                }
            }
            position = Vector3.zero; return false;
        }
    }
}
