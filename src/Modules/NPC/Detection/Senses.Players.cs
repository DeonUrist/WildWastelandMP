using System.Collections.Generic;
using UnityEngine;

namespace WildWasteland.Modules.NPC
{
    internal static partial class Senses
    {
        internal sealed class PlayerContact
        {
            internal float SeenFor, CheckedAt = -1000f, VisibleAt = -1000f, FoughtUntil;
            internal bool Acquired;
            internal float ContactUntil, ContactAt = -1000f;
            internal Vector3 WorldPosition;
        }
        static PlayerContact Contact(Agent agent, GameObject player)
        {
            PlayerContact contact;
            if (!agent.Players.TryGetValue(player, out contact)) { contact = new PlayerContact(); agent.Players.Add(player, contact); }
            return contact;
        }
        static bool PlayerAvailable(GameObject player)
        { return player != null && player.activeInHierarchy && player.layer != 5 && (player == _player ? Core.Multiplayer.LocalTargetable : Core.Multiplayer.IsRemotePlayer(player) && Core.Multiplayer.RemoteTargetable(player)); }
        static GameObject PlayerVehicle(GameObject player)
        { return IsPlayer(player) ? Core.Multiplayer.IsRemotePlayer(player) ? Core.Multiplayer.RemoteCar(player) : _playerCar : null; }
        static bool PlayerReady(Agent agent, GameObject player)
        { PlayerContact contact; return player != null && agent.Players.TryGetValue(player, out contact) && contact.Acquired; }
        static float PlayerMemoryUntil(Agent agent, GameObject player)
        { PlayerContact contact; return player != null && agent.Players.TryGetValue(player, out contact) ? contact.FoughtUntil : ReferenceEquals(agent.Fought, player) ? agent.FoughtUntil : 0; }
        static bool TracksPlayer(Agent agent, GameObject player, float now)
        {
            PlayerContact contact;
            return player != null && agent.Players.TryGetValue(player, out contact) && contact.Acquired && now - contact.VisibleAt < Mathf.Max(5f, NpcModule.LoseSeconds.Value);
        }
        static float PlayerNotice(Agent agent, GameObject player)
        { PlayerContact contact; return player != null && agent.Players.TryGetValue(player, out contact) ? contact.SeenFor : 0; }
        static void ConsiderPlayer(Agent agent, GameObject player, Vector3 eye, float interval, float now, ref GameObject best, ref float distance)
        {
            if (player == null) return;
            float d2 = (player.transform.position - agent.T.position).sqrMagnitude;
            bool available = PlayerAvailable(player);
            bool visible = available && Visible(agent, eye, player, true, InStormOf(player));
            PlayerContact contact;
            if (!agent.Players.TryGetValue(player, out contact)) { if (!visible) return; contact = Contact(agent, player); }
            if (interval > 0 && contact.CheckedAt != now)
            {
                float elapsed = contact.CheckedAt < 0 ? interval : Mathf.Max(0, now - contact.CheckedAt);
                contact.SeenFor = visible ? Mathf.Max(0, contact.SeenFor - Mathf.Max(0, elapsed - interval)) + Mathf.Min(interval, elapsed) : Mathf.Max(0, contact.SeenFor - elapsed);
                contact.CheckedAt = now;
                if (visible)
                {
                    contact.VisibleAt = now;
                    if (agent.CaveWeb || contact.SeenFor >= NoticeNeed(Mathf.Sqrt(d2))) contact.Acquired = true;
                }
                else if (!available || now - contact.VisibleAt >= Mathf.Max(5f, NpcModule.LoseSeconds.Value)) contact.Acquired = false;
                if (!available) contact.SeenFor = 0;
            }
            // Advance every visible player's own timer before choosing the nearest eligible target.
            if (visible && (contact.Acquired || agent.Target == player || Remembers(agent, player, now)) && d2 < distance) { best = player; distance = d2; }
        }
        static GameObject NearestVisiblePlayer(Agent agent, Vector3 eye, float interval, float now, out float distance)
        {
            GameObject best = null; distance = float.MaxValue;
            if (!agent.Hostile.Contains("Player")) return null;
            ConsiderPlayer(agent, _player, eye, interval, now, ref best, ref distance);
            for (int i = 0; i < Core.Multiplayer.RemoteCount; i++) ConsiderPlayer(agent, Core.Multiplayer.RemoteAt(i), eye, interval, now, ref best, ref distance);
            return best;
        }
        static GameObject NearestRememberedPlayer(Agent agent, float now)
        {
            GameObject best = null; float distance = float.MaxValue;
            foreach (var pair in agent.Players)
            {
                if (now >= pair.Value.FoughtUntil || !PlayerAvailable(pair.Key)) continue;
                float d = (pair.Key.transform.position - agent.T.position).sqrMagnitude;
                if (d < distance) { distance = d; best = pair.Key; }
            }
            if (best == null && IsPlayer(agent.Fought) && Remembers(agent, agent.Fought, now)) best = agent.Fought;
            return best;
        }
        static readonly List<GameObject> ExpiredPlayers = new List<GameObject>();
        static void PrunePlayers(Agent agent, float now)
        {
            ExpiredPlayers.Clear();
            foreach (var pair in agent.Players)
                if (!PlayerAvailable(pair.Key) || (now >= Mathf.Max(pair.Value.FoughtUntil, pair.Value.ContactUntil) && now - pair.Value.CheckedAt > Mathf.Max(5f, NpcModule.LoseSeconds.Value) && pair.Key != agent.Target)) ExpiredPlayers.Add(pair.Key);
            foreach (var player in ExpiredPlayers) agent.Players.Remove(player);
            ExpiredPlayers.Clear();
            if (!PlayerAvailable(agent.Fought)) { agent.Fought = null; agent.FoughtUntil = 0; }
        }
    }
}
