using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WildWasteland.Core
{
    // Only the local host UI can initiate a reset; clients accept directed host placement, never requests.
    internal static class MultiplayerPlayerReset
    {
        const byte Command = 171, Result = 172;
        sealed class Work { internal ulong Token, Epoch; internal object Record; internal string Name; }
        static readonly Dictionary<ulong, Work> Waiting = new Dictionary<ulong, Work>();
        static Func<ulong, object> _record;
        static Action _report;
        static Action<string> _notice;
        static ulong _clientToken, _clientEpoch;
        static bool _running, _clientResult;
        internal static bool Pending { get { return Waiting.Count != 0 || MultiplayerRespawn.Resetting; } }
        internal static void Install()
        {
            var session = Multiplayer.Type("Net.NetSession");
            _record = MultiplayerAccess.Call<Func<ulong, object>>(session, "RecordOf", typeof(ulong));
            _report = MultiplayerAccess.Call<Action>(session, "SendPlayerSave", Type.EmptyTypes);
            _notice = MultiplayerAccess.Call<Action<string>>(Multiplayer.Type("UI.Chat"), "Notice", typeof(string));
            Multiplayer.Message += Receive; Multiplayer.SessionChanged += Changed;
        }
        static void Changed() { if (!Multiplayer.SessionActive) { Waiting.Clear(); _clientToken = _clientEpoch = 0; _running = _clientResult = false; } }
        internal static bool CanReset(MultiplayerRoster.Player row)
        {
            if (!Multiplayer.Hosting || !Multiplayer.WorldReady || row == null || row.Epoch != MultiplayerParts.Epoch || Pending
                || SaveBus.Busy || MultiplayerSaving.Frozen || HostSaveSystem.Requested) return false;
            if (row.Sleeping) return MultiplayerSleepers.CanRemoveBody(row.SleeperId, row.Record);
            Vector3 camp;
            if (!row.HasPosition || !MultiplayerRespawn.StartingPosition(out camp)) return false;
            return row.Peer == 0 ? GameRefs.Player != null : ReferenceEquals(_record(row.Peer), row.Record) && MultiplayerSleepers.ReadyPeer(row.Peer) && Multiplayer.PlayerFor(row.Peer) != null;
        }
        internal static bool Reset(MultiplayerRoster.Player row)
        {
            if (!CanReset(row)) return false;
            if (row.Sleeping)
            {
                if (!MultiplayerSleepers.RemoveBody(row.SleeperId, row.Record)) return false;
                MultiplayerRoster.Invalidate(); _notice("Sleeping body removed for " + row.Name + ". Saved character retained."); return true;
            }
            Vector3 target; if (!MultiplayerRespawn.StartingPosition(out target)) return false;
            var work = new Work { Token = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0), Epoch = MultiplayerParts.Epoch, Record = row.Record, Name = row.Name };
            Waiting[row.Peer] = work;
            if (row.Peer == 0) Scheduler.StartCoroutine(Local(work));
            else
            {
                Multiplayer.Send(Command, w => { w.Write(work.Epoch); w.Write(work.Token); w.Write(target.x); w.Write(target.y); w.Write(target.z); }, row.Peer);
                Scheduler.StartCoroutine(Watch(row.Peer, work));
            }
            _notice("Resetting " + row.Name + " to the starting location.");
            Log.Always("[Multiplayer] Host player reset requested: " + row.Name + " [peer " + row.Peer + "]"); return true;
        }
        static IEnumerator Local(Work work)
        {
            Vector3 target; bool success = false;
            try
            {
                if (MultiplayerRespawn.StartingPosition(out target))
                { yield return MultiplayerRespawn.ResetToCamp(target); success = MultiplayerRespawn.ResetSucceeded; }
            }
            finally { Complete(0, work, success); }
        }
        static IEnumerator Watch(ulong peer, Work work)
        {
            Work current;
            while (Waiting.TryGetValue(peer, out current) && ReferenceEquals(current, work))
            {
                if (!Multiplayer.Hosting || !Multiplayer.WorldReady || MultiplayerParts.Epoch != work.Epoch || !ReferenceEquals(_record(peer), work.Record))
                { Waiting.Remove(peer); if (Multiplayer.Hosting) _notice("Reset cancelled: " + work.Name + " disconnected or the world changed."); yield break; }
                yield return new WaitForSecondsRealtime(.5f);
            }
        }
        static void Complete(ulong peer, Work work, bool success)
        {
            Work current;
            if (!Waiting.TryGetValue(peer, out current) || !ReferenceEquals(current, work)) return;
            Waiting.Remove(peer);
            if (!Multiplayer.Hosting || work.Epoch != MultiplayerParts.Epoch) return;
            MultiplayerRoster.Invalidate();
            _notice(success ? work.Name + " moved to the starting location." : "Could not reset " + work.Name + ".");
            if (success) MultiplayerRecovery.Checkpoint();
            Log.Always("[Multiplayer] Host player reset " + (success ? "completed: " : "failed: ") + work.Name);
        }
        static void Receive(ulong from, byte kind, BinaryReader r)
        {
            if (kind != Command && kind != Result) return;
            ulong epoch = r.ReadUInt64(), token = r.ReadUInt64();
            if (epoch == 0 || epoch != MultiplayerParts.Epoch || token == 0) return;
            if (kind == Result)
            {
                bool success = r.ReadBoolean(); Work work;
                if (Multiplayer.Hosting && r.BaseStream.Position == r.BaseStream.Length && Waiting.TryGetValue(from, out work)
                    && work.Epoch == epoch && work.Token == token && ReferenceEquals(_record(from), work.Record)) Complete(from, work, success);
                return;
            }
            var target = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            if (!Multiplayer.Client || from != 0 || r.BaseStream.Position != r.BaseStream.Length || float.IsNaN(target.sqrMagnitude) || float.IsInfinity(target.sqrMagnitude)) return;
            if (_clientEpoch == epoch && _clientToken == token) { if (!_running) Reply(epoch, token, _clientResult); return; }
            if (_running) { Reply(epoch, token, false); return; }
            _clientEpoch = epoch; _clientToken = token; _clientResult = false; _running = true;
            Scheduler.StartCoroutine(Client(target, epoch, token));
        }
        static void Reply(ulong epoch, ulong token, bool success)
        { Multiplayer.Send(Result, w => { w.Write(epoch); w.Write(token); w.Write(success); }, 0); }
        static IEnumerator Client(Vector3 target, ulong epoch, ulong token)
        {
            bool success = false;
            try
            {
                while (Multiplayer.Client && MultiplayerParts.Epoch == epoch && (MultiplayerSaving.Frozen || SaveBus.Busy)) yield return null;
                if (Multiplayer.Client && Multiplayer.WorldReady && MultiplayerParts.Epoch == epoch && MultiplayerParty.PlayerReady && !MultiplayerSleepers.AwaitingWake)
                { yield return MultiplayerRespawn.ResetToCamp(target); success = MultiplayerRespawn.ResetSucceeded; if (success) _report(); }
            }
            finally
            {
                if (_clientEpoch == epoch && _clientToken == token)
                {
                    _running = false; _clientResult = success;
                    if (Multiplayer.Client && MultiplayerParts.Epoch == epoch)
                    { _notice(success ? "The host moved you to the starting location." : "The host reset could not complete."); Reply(epoch, token, success); }
                }
            }
        }
    }
}
