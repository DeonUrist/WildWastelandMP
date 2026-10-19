using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WildWasteland.Core
{
    internal static partial class MultiplayerSleepers
    {
        const byte WakePacket = 165, ConnectedPacket = 166;
        sealed class Wake
        {
            internal Sleeper Sleeper; internal ulong Token; internal bool Granted; internal Vector3 Position;
        }
        static readonly Dictionary<ulong, Wake> Wakes = new Dictionary<ulong, Wake>();
        sealed class JoiningPlayer { internal int Id; internal Vector3 Position; internal float Yaw; }
        static readonly Dictionary<ulong, JoiningPlayer> Joining = new Dictionary<ulong, JoiningPlayer>();
        static readonly HashSet<ulong> NewJoining = new HashSet<ulong>();
        static Func<object, object> _peerRecord, _pending;
        static Func<object, ulong?> _joinSpawn;
        static Func<object, byte> _peerId;
        static Func<object, ulong> _remotePeer;
        static Func<object, GameObject> _remoteRoot;
        static Func<ulong, object> _recordOf;
        static Func<object, IDictionary> _connections;
        static Action<string> _endJoin;
        static Action<string> _joinNotice;
        static Func<ulong, string> _peerName;
        static Func<object, ulong> _infoId;
        static Action<object, bool> _infoHas;
        static Action<object, Vector3> _infoPosition;
        static Action<object, float> _infoDistance;
        static Action<object, string> _rejectJoin;
        static Func<object, IList> _savedItems;
        static Func<object, int> _savedItemId;
        static Func<object, string> _savedSlot;
        static Func<List<KeyValuePair<int, string>>> _localInventory;
        static object _restoreRecord;
        static int _wakeId;
        static ulong _wakeToken, _wakeEpoch;
        static bool _restored, _placed, _requested, _finishing, _connected;
        static float _nextOffer, _nextWakeRequest;
        static byte _playerSave;
        internal static bool AwaitingWake { get { return _wakeId != 0; } }
        static void InstallWake(Type session, Type record)
        {
            var peer = Multiplayer.Type("Net.NetSession+Peer"); var remote = Multiplayer.Type("Game.RemotePlayer");
            _peerRecord = MultiplayerAccess.Get<object>(peer, "Record"); _peerId = MultiplayerAccess.Get<byte>(peer, "Id");
            _pending = MultiplayerAccess.Get<object>(session, "pendingRestore");
            _joinSpawn = MultiplayerAccess.Get<ulong?>(session, "spawnNextToPeer");
            _recordOf = MultiplayerAccess.Call<Func<ulong, object>>(session, "RecordOf", typeof(ulong));
            _connections = MultiplayerAccess.Get<IDictionary>(session, "byConnection");
            _remotePeer = MultiplayerAccess.Get<ulong>(remote, "peer"); _remoteRoot = MultiplayerAccess.Get<GameObject>(remote, "root");
            _endJoin = MultiplayerAccess.Call<Action<string>>(session, "End", typeof(string));
            _joinNotice = MultiplayerAccess.Call<Action<string>>(Multiplayer.Type("UI.Chat"), "Notice", typeof(string));
            _peerName = MultiplayerAccess.Call<Func<ulong, string>>(session, "NameOf", typeof(ulong));
            var info = Multiplayer.Type("Net.NetSession+PlayerInfo");
            _infoId = MultiplayerAccess.Get<ulong>(info, "Id"); _infoHas = MultiplayerAccess.Set<bool>(info, "HasPosition");
            _infoPosition = MultiplayerAccess.Set<Vector3>(info, "LocalPosition"); _infoDistance = MultiplayerAccess.Set<float>(info, "DistanceMeters");
            _rejectJoin = MultiplayerAccess.Call<Action<object, string>>(session, "Reject", peer, typeof(string));
            _savedItems = MultiplayerAccess.Get<IList>(record, "Items");
            _savedItemId = MultiplayerAccess.Get<int>(Multiplayer.Type("Worlds.SavedItem"), "Id");
            _savedSlot = MultiplayerAccess.Get<string>(Multiplayer.Type("Worlds.SavedItem"), "Slot");
            _localInventory = MultiplayerAccess.Call<Func<List<KeyValuePair<int, string>>>>(Multiplayer.Type("Net.WorldObjects"), "LocalInventory", Type.EmptyTypes);
            _playerSave = Convert.ToByte(Enum.Parse(Multiplayer.Type("Net.MessageType"), "PlayerSave"));
            Require(session, "Welcome", "Welcomed");
            Require(session, "Players", "PlayerPositions");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(session, "SendPlayerSave"), prefix: AccessTools.Method(typeof(MultiplayerSleepers), "CanReport"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(Multiplayer.Type("Net.WorldObjects"), "RefreshInventory"), prefix: AccessTools.Method(typeof(MultiplayerSleepers), "CanRefresh"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(remote, "Update"), prefix: AccessTools.Method(typeof(MultiplayerSleepers), "RemoteUpdating")))
                throw new InvalidOperationException("Sleeping-player spawn handoff hooks failed");
            Multiplayer.Message += ReceiveWake;
        }
        static bool Online(object record)
        { foreach (DictionaryEntry entry in _connections(null)) if (ReferenceEquals(_peerRecord(entry.Value), record)) return true; return false; }
        internal static bool WelcomedPeer(ulong peer) { return _recordOf != null && _recordOf(peer) != null; }
        internal static bool ReadyPeer(ulong peer) { return WelcomedPeer(peer) && !Wakes.ContainsKey(peer) && !NewJoining.Contains(peer); }
        internal static bool JoiningPosition(ulong peer, out Vector3 position, out float yaw)
        {
            Wake wake; JoiningPlayer joining; Sleeper sleeper;
            if (Wakes.TryGetValue(peer, out wake)) sleeper = wake.Sleeper;
            else if (Joining.TryGetValue(peer, out joining))
            {
                if (!Bodies.TryGetValue(joining.Id, out sleeper)) { position = Origin.Scene(joining.Position); yaw = joining.Yaw; return true; }
            }
            else { position = Vector3.zero; yaw = 0; return false; }
            position = sleeper.Body != null ? sleeper.Body.Bodies[0].position : Origin.Scene(sleeper.Positions[0]);
            yaw = sleeper.Record != null ? _yaw(sleeper.Record) : sleeper.Rotations[0].eulerAngles.y;
            return true;
        }
        static void PlayerPositions(object __result)
        {
            if (!Multiplayer.Active) return;
            foreach (object info in (IList)__result)
            {
                Vector3 position; float yaw;
                if (!JoiningPosition(_infoId(info), out position, out yaw)) continue;
                _infoHas(info, true); _infoPosition(info, position);
                if (GameRefs.Player != null) _infoDistance(info, Vector3.Distance(position, GameRefs.Player.transform.position));
            }
        }
        static void ClearWake()
        {
            // Authority epoch negotiation may finish after native restore, within the same connection.
            if (!Multiplayer.Client || !Multiplayer.SessionActive) { _restoreRecord = null; _restored = _placed = false; }
            Wakes.Clear(); Joining.Clear(); NewJoining.Clear(); _connected = false; _wakeId = 0; _wakeToken = _wakeEpoch = 0;
            _requested = _finishing = false; _nextOffer = _nextWakeRequest = 0;
        }
        static bool CanRefresh(object __0)
        { if (!Multiplayer.Hosting) return true; foreach (var sleeper in Bodies.Values) if (ReferenceEquals(sleeper.Record, __0)) return false; return true; }
        static bool CanReport()
        {
            if (!Multiplayer.Client) return true;
            var record = _restoreRecord ?? _pending(null);
            return !AwaitingWake && (record == null || !_stats(record).ContainsKey(SleepingKey));
        }
        internal static bool AllowReport(ulong from, byte[] data)
        { return !Multiplayer.Hosting || data[0] != _playerSave || !Wakes.ContainsKey(from); }
        static bool RemoteUpdating(object __instance)
        {
            ulong peer = _remotePeer(__instance);
            if (!Wakes.ContainsKey(peer) && !Joining.ContainsKey(peer)) return true;
            var root = _remoteRoot(__instance); if (root != null) root.SetActive(false);
            return false;
        }
        static void Welcomed(object __0)
        {
            if (!Multiplayer.Hosting) return;
            var record = _peerRecord(__0); if (record == null) return;
            foreach (var sleeper in Bodies.Values) if (ReferenceEquals(sleeper.Record, record))
            {
                ulong peer = _peerId(__0);
                Wake existing; if (Wakes.TryGetValue(peer, out existing) && ReferenceEquals(existing.Sleeper, sleeper)) return;
                var wake = new Wake { Sleeper = sleeper, Token = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0) };
                Wakes[peer] = wake; Offer(peer, wake);
                Log.Always("[Multiplayer] Sleeper join offered: " + sleeper.Name);
                foreach (object item in _savedItems(record)) if (_savedItemId(item) == 0)
                { _rejectJoin(__0, "Could not recreate all saved inventory objects. Your sleeping body and saved state were retained."); break; }
                return;
            }
            NewJoining.Add(_peerId(__0));
        }
        static void Offer(ulong peer, Wake wake)
        {
            var position = wake.Sleeper.Body != null ? Origin.World(wake.Sleeper.Body.Bodies[0].position) : wake.Sleeper.Positions[0];
            Multiplayer.Send(WakePacket, w => { w.Write(MultiplayerParts.Epoch); w.Write((byte)0); w.Write(wake.Sleeper.Id); w.Write(wake.Token); w.Write(peer); Vector(w, position); w.Write(_yaw(wake.Sleeper.Record)); });
        }
        static void CancelWake(ulong peer)
        {
            NewJoining.Remove(peer);
            Wake wake; if (!Wakes.TryGetValue(peer, out wake)) return;
            Wakes.Remove(peer); wake.Sleeper.Place = true; wake.Sleeper.NextGround = 0; Store(wake.Sleeper);
            Multiplayer.Send(WakePacket, w => { w.Write(MultiplayerParts.Epoch); w.Write((byte)4); w.Write(wake.Sleeper.Id); w.Write(wake.Token); w.Write(peer); });
        }
        static bool WakeLocked(Sleeper sleeper)
        { foreach (var wake in Wakes.Values) if (ReferenceEquals(wake.Sleeper, sleeper) && wake.Granted) return true; return false; }
        internal static void RestoreStarted(object record)
        {
            if (record == null || ReferenceEquals(_restoreRecord, record)) return;
            _restoreRecord = record; _restored = _placed = false;
        }
        internal static void RestoreFinished(object record, bool complete)
        {
            _restored = complete;
            if (!complete && _stats(record).ContainsKey(SleepingKey))
                _endJoin("Could not restore all inventory objects. Your sleeping body and saved state were retained; reconnect to try again.");
        }
        internal static void InitialPlaced() { _placed = true; }
        static bool ReadyToWake()
        { return Multiplayer.Client && Multiplayer.WorldReady && MultiplayerParty.PlayerReady && !LoadingProgress.MenusBlocked && !MultiplayerSaving.Frozen && !SaveBus.Busy; }
        static bool InventoryRestored()
        {
            if (_restoreRecord == null) return false;
            var inventory = _localInventory();
            foreach (object item in _savedItems(_restoreRecord))
            {
                bool found = false;
                foreach (var current in inventory) if (current.Key == _savedItemId(item) && current.Value == _savedSlot(item)) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }
        static void TickWake()
        {
            if (Multiplayer.Hosting)
            {
                if (Time.unscaledTime < _nextOffer) return;
                _nextOffer = Time.unscaledTime + 3f;
                foreach (var pair in Wakes) Offer(pair.Key, pair.Value);
            }
            else if (AwaitingWake && !_finishing && Time.unscaledTime >= _nextWakeRequest && _restored && _placed && ReadyToWake())
            {
                _nextWakeRequest = Time.unscaledTime + 1f;
                if (!InventoryRestored()) { _endJoin("Inventory restoration did not fill every saved slot. Your sleeping body and saved state were retained."); return; }
                if (!MultiplayerItems.FlushInventory()) return;
                if (!_requested) Log.Always("[Multiplayer] Sleeper join ready: inventory and initial placement complete");
                _requested = true;
                Multiplayer.Send(WakePacket, w => { w.Write(_wakeEpoch); w.Write((byte)1); w.Write(_wakeId); w.Write(_wakeToken); });
            }
            else if (!_connected && !AwaitingWake && (_restoreRecord == null || _restored && !_stats(_restoreRecord).ContainsKey(SleepingKey)) && !_joinSpawn(null).HasValue && ReadyToWake() && Time.unscaledTime >= _nextWakeRequest)
            {
                _nextWakeRequest = Time.unscaledTime + 1f;
                Multiplayer.Send(WakePacket, w => { w.Write(MultiplayerParts.Epoch); w.Write((byte)5); w.Write(0); w.Write((ulong)0); });
            }
        }
        internal static void WakePlaced(Vector3 position)
        { if (AwaitingWake && _finishing) Scheduler.StartCoroutine(ConfirmWake(_wakeEpoch, _wakeId, _wakeToken, position)); }
        static IEnumerator ConfirmWake(ulong epoch, int id, ulong token, Vector3 position)
        {
            while (Multiplayer.Client && _wakeEpoch == epoch && _wakeId == id && _wakeToken == token)
            {
                if (ReadyToWake()) Multiplayer.Send(WakePacket, w => { w.Write(epoch); w.Write((byte)3); w.Write(id); w.Write(token); Vector(w, position); });
                yield return new WaitForSecondsRealtime(1f);
            }
        }
        static void WakeRemoved(int id)
        {
            ulong peer = Nobody; foreach (var pair in Joining) if (pair.Value.Id == id) { peer = pair.Key; break; }
            if (peer != Nobody) Joining.Remove(peer);
            if (_wakeId != id) return;
            if (_restoreRecord != null) _stats(_restoreRecord).Remove(SleepingKey);
            _wakeId = 0; _wakeToken = 0; _finishing = false; _connected = true;
        }
        static void Connected(ulong peer, string name)
        {
            _joinNotice(name + " has connected");
            Multiplayer.Send(ConnectedPacket, w => { w.Write(MultiplayerParts.Epoch); w.Write(peer); w.Write(name); });
        }
        static void ReceiveWake(ulong from, byte kind, BinaryReader r)
        {
            if (kind == ConnectedPacket)
            {
                ulong noticeEpoch = r.ReadUInt64(), peer = r.ReadUInt64(); string name = r.ReadString();
                if (!Multiplayer.Client || from != 0 || noticeEpoch != MultiplayerParts.Epoch || noticeEpoch == 0 || name.Length > 48 || r.BaseStream.Position != r.BaseStream.Length) return;
                if (peer == Multiplayer.LocalPeer) _connected = true;
                _joinNotice(name + " has connected"); return;
            }
            if (kind != WakePacket) return;
            ulong epoch = r.ReadUInt64(); byte mode = r.ReadByte(); int id = r.ReadInt32(); ulong token = r.ReadUInt64();
            if (epoch == 0 || epoch != MultiplayerParts.Epoch || (id <= 0 && mode != 5)) return;
            if (Multiplayer.Client && from == 0)
            {
                if (_epoch != epoch) { Clear(); _epoch = epoch; }
                if (mode == 0 || mode == 4)
                {
                    ulong peer = r.ReadUInt64();
                    if (mode == 4) { if (r.BaseStream.Position != r.BaseStream.Length) return; JoiningPlayer old; if (Joining.TryGetValue(peer, out old) && old.Id == id) Joining.Remove(peer); return; }
                    var position = Vector(r); float yaw = r.ReadSingle();
                    if (!Finite(yaw) || r.BaseStream.Position != r.BaseStream.Length) return;
                    if (Removed.Contains(id)) return;
                    JoiningPlayer joining;
                    if (!Joining.TryGetValue(peer, out joining)) { joining = new JoiningPlayer(); Joining.Add(peer, joining); }
                    joining.Id = id; joining.Position = position; joining.Yaw = yaw;
                    if (peer == Multiplayer.LocalPeer && _wakeId == 0) { _wakeEpoch = epoch; _wakeId = id; _wakeToken = token; }
                }
                else if (mode == 2)
                {
                    var point = Vector(r);
                    if (r.BaseStream.Position != r.BaseStream.Length || _wakeId != id || _wakeToken != token || _finishing || !_requested) return;
                    _finishing = true; Scheduler.StartCoroutine(MultiplayerRespawn.FinishWake(point));
                }
                return;
            }
            if (!Multiplayer.Hosting || from == 0 || MultiplayerSaving.Frozen || SaveBus.Busy) return;
            if (mode == 5 && id == 0 && token == 0 && r.BaseStream.Position == r.BaseStream.Length)
            {
                if (WelcomedPeer(from) && Multiplayer.PlayerFor(from) != null && NewJoining.Remove(from)) Connected(from, _peerName(from));
                return;
            }
            Wake wake;
            if (!Wakes.TryGetValue(from, out wake) || wake.Token != token || wake.Sleeper.Id != id || !ReferenceEquals(_recordOf(from), wake.Sleeper.Record)) return;
            if (mode == 1 && r.BaseStream.Position == r.BaseStream.Length)
            {
                if (wake.Sleeper.Body == null) return;
                if (!wake.Granted)
                {
                    wake.Sleeper.Carrier = Nobody; Capture(wake.Sleeper); Store(wake.Sleeper);
                    wake.Position = _position(wake.Sleeper.Record); wake.Granted = true; Publish(wake.Sleeper, true);
                    Log.Always("[Multiplayer] Sleeper spawn granted: " + wake.Sleeper.Name);
                }
                Multiplayer.Send(WakePacket, w => { w.Write(epoch); w.Write((byte)2); w.Write(id); w.Write(token); Vector(w, wake.Position); }, from);
            }
            else if (mode == 3)
            {
                var point = Vector(r);
                if (!wake.Granted || r.BaseStream.Position != r.BaseStream.Length || (point - wake.Position).sqrMagnitude > 100f || !MultiplayerPlayers.At(from, point)) return;
                var sleeper = wake.Sleeper;
                _setPosition(sleeper.Record, point); _setHasPosition(sleeper.Record, true); _stats(sleeper.Record).Remove(SleepingKey);
                Wakes.Remove(from); Destroy(sleeper); Bodies.Remove(id);
                MultiplayerRecovery.Checkpoint();
                Multiplayer.Send(Remove, w => { w.Write(epoch); w.Write(id); });
                Connected(from, sleeper.Name);
                Log.Always("[Multiplayer] Sleeping player replaced after confirmed spawn: " + sleeper.Name);
            }
        }
    }
}
