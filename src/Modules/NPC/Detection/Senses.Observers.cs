using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.NPC
{
    internal static partial class Senses
    {
        sealed class Observer
        {
            internal Agent Agent;
            internal FsmFloat Health;
            internal float Next, Last = -1f;
            internal NpcPlayerSight Sight;
        }
        sealed class Transfer
        {
            internal GameObject Player;
            internal Vector3 World;
            internal float Until, SeenAt;
            internal bool Seen;
        }
        static readonly Dictionary<GameObject, Observer> Observers = new Dictionary<GameObject, Observer>();
        static readonly Dictionary<GameObject, List<Transfer>> Transfers = new Dictionary<GameObject, List<Transfer>>();
        static readonly List<GameObject> ObserverGone = new List<GameObject>();
        static readonly RaycastHit[] ObserverHits = new RaycastHit[48];
        static void ResetObservers() { Observers.Clear(); Transfers.Clear(); ObserverGone.Clear(); }
        static Agent SeatedObserver(GameObject owner)
        { Observer view; return Multiplayer.Hosting && owner != null && owner.transform.parent != null && Observers.TryGetValue(owner, out view) ? view.Agent : null; }
        internal static void SetSeatedTarget(GameObject owner, GameObject target)
        {
            Observer view; if (!Multiplayer.Hosting || owner == null || !Observers.TryGetValue(owner, out view)) return;
            var a = view.Agent;
            if (IsPlayer(target) && (view.Sight.Player != target || !view.Sight.Visible || !PlayerAvailable(target))) target = null;
            a.Target = target; a.State = target != null ? State.Combat : State.Idle; a.UnseenFor = 0; a.UnseenSince = -1;
            if (a.DetectedVar != null) a.DetectedVar.Value = target;
        }
        internal static void SweepObservers()
        {
            ObserverGone.Clear(); foreach (var pair in Observers) if (pair.Key == null || !pair.Key.activeInHierarchy || pair.Key.transform.parent == null) ObserverGone.Add(pair.Key);
            foreach (var owner in ObserverGone) Observers.Remove(owner);
            ObserverGone.Clear();
            foreach (var pair in Transfers)
            { pair.Value.RemoveAll(ExpiredTransfer); if (pair.Key == null || pair.Value.Count == 0) ObserverGone.Add(pair.Key); }
            foreach (var owner in ObserverGone) Transfers.Remove(owner); ObserverGone.Clear();
        }
        static bool ExpiredTransfer(Transfer transfer) { return transfer.Player == null || transfer.Until <= Time.time; }
        internal static NpcPlayerSight ObservePlayers(GameObject observer, GameObject vehicle, Transform facing, bool hostile, float memorySeconds)
        {
            if (!On || !Multiplayer.Hosting || observer == null || !observer.activeInHierarchy) return default(NpcPlayerSight);
            Observer view;
            if (!Observers.TryGetValue(observer, out view))
            {
                var health = FsmUtil.Find(observer, "Health");
                var agent = new Agent { Owner = observer, T = facing != null ? facing : observer.transform, Human = true,
                    Hostile = new HashSet<string>(), SightOwner = vehicle != null ? vehicle.transform.root : observer.transform.root };
                agent.Detection = FsmUtil.Find(observer, "Detection");
                agent.DetectedVar = agent.Detection != null ? agent.Detection.FsmVariables.FindFsmGameObject("detectedObj") : null;
                foreach (var node in observer.GetComponentsInChildren<Transform>(true))
                    if (node.name == "mixamorig:Head" || node.name == "Head") { agent.Head = node; break; }
                view = new Observer { Agent = agent, Health = health != null ? health.FsmVariables.FindFsmFloat("Health") : null };
                Observers.Add(observer, view); ApplyObserverTransfer(agent);
            }
            if (!hostile || (view.Health != null && view.Health.Value <= 0))
            { view.Agent.Players.Clear(); view.Agent.Target = null; view.Sight = default(NpcPlayerSight); view.Next = 0; return view.Sight; }
            float now = Time.time;
            if (now < view.Next && (view.Sight.Player == null || PlayerAvailable(view.Sight.Player))) return view.Sight;
            float interval = view.Last < 0 ? .25f : Mathf.Min(.5f, Mathf.Max(0, now - view.Last));
            view.Last = now; view.Next = now + .25f;
            var a = view.Agent; a.T = facing != null ? facing : observer.transform;
            a.SightOwner = vehicle != null ? vehicle.transform.root : observer.transform.root;
            a.Hostile.Add("Player"); a.InStorm = Storm.Any && Storm.In(a.T.position);
            PrunePlayers(a, now); FindPlayer();
            float distance; var visible = NearestVisiblePlayer(a, a.Head != null ? a.Head.position : a.T.position + Vector3.up * 1.3f, interval, now, out distance);
            RefreshObserverContact(a, _player, now, memorySeconds);
            for (int i = 0; i < Multiplayer.RemoteCount; i++) RefreshObserverContact(a, Multiplayer.RemoteAt(i), now, memorySeconds);
            GameObject best = visible; float bestDistance = float.MaxValue;
            if (best == null) foreach (var pair in a.Players)
            {
                if (!PlayerAvailable(pair.Key) || pair.Value.ContactUntil <= now) continue;
                float d = (Origin.Scene(pair.Value.WorldPosition) - a.T.position).sqrMagnitude;
                if (d < bestDistance) { best = pair.Key; bestDistance = d; }
            }
            view.Sight = default(NpcPlayerSight);
            if (best != null)
            {
                var contact = a.Players[best];
                view.Sight = new NpcPlayerSight { Player = best, WorldPosition = contact.WorldPosition, Visible = best == visible,
                    Heard = best != visible && contact.ContactAt == now, Until = contact.ContactUntil, At = contact.ContactAt };
            }
            return view.Sight;
        }
        static void RefreshObserverContact(Agent a, GameObject player, float now, float memory)
        {
            if (!PlayerAvailable(player)) return;
            PlayerContact contact; bool seen = a.Players.TryGetValue(player, out contact) && contact.VisibleAt == now && (contact.Acquired || Remembers(a, player, now));
            bool heard = false; var car = PlayerVehicle(player);
            if (car != null)
            {
                float radius = 0; string about;
                if (Apocapatrol_EngineRunning(car)) radius = EngineRadius(car, out about);
                if (Multiplayer.CarHorn(car)) radius = Mathf.Max(radius, NpcModule.EngineMaxRange.Value + 50f);
                if (a.InStorm || InStormOf(player)) radius *= Storm.SightFactor;
                heard = radius > 0 && (player.transform.position - a.T.position).sqrMagnitude <= radius * radius;
            }
            if (!seen && !heard) return;
            contact = Contact(a, player); contact.ContactAt = now; contact.ContactUntil = Mathf.Max(contact.ContactUntil, now + Mathf.Max(0f, memory));
            contact.WorldPosition = Origin.World(player.transform.position);
            if (seen) contact.FoughtUntil = Mathf.Max(contact.FoughtUntil, contact.ContactUntil);
        }
        static bool ObserverSight(Vector3 from, Vector3 to, Transform target, Transform own)
        {
            var delta = to - from; float distance = delta.magnitude; if (distance < .05f) return true;
            int count = Physics.RaycastNonAlloc(from, delta / distance, ObserverHits, distance, SightBlockers, QueryTriggerInteraction.Ignore);
            if (count == ObserverHits.Length) return false;
            var car = PlayerVehicle(target.gameObject);
            for (int i = 0; i < count; i++)
            {
                var hit = ObserverHits[i].collider; if (hit == null) continue; var t = hit.transform;
                if (t.IsChildOf(own) || t.IsChildOf(target) || (car != null && t.IsChildOf(car.transform.root))) continue;
                return false;
            }
            return true;
        }
        internal static void TransferPlayerAwareness(GameObject source, GameObject destination, float seconds)
        {
            if (!On || !Multiplayer.Hosting || source == null || destination == null || seconds <= 0) return;
            Observer view; bool seated = source.transform.parent != null && Observers.TryGetValue(source, out view);
            var a = seated ? SeatedObserver(source) : Get(source); if (a == null) return;
            var pending = new List<Transfer>(); float now = Time.time;
            foreach (var pair in a.Players)
            {
                if (!PlayerAvailable(pair.Key)) continue;
                float until = Mathf.Min(now + seconds, Mathf.Max(pair.Value.ContactUntil, pair.Value.FoughtUntil));
                if (until <= now) continue;
                pending.Add(new Transfer { Player = pair.Key, World = seated ? pair.Value.WorldPosition : Origin.World(pair.Key.transform.position),
                    Until = until, SeenAt = pair.Value.VisibleAt, Seen = pair.Value.Acquired || pair.Value.FoughtUntil > now });
            }
            if (pending.Count == 0) return;
            Transfers[destination] = pending;
            var receiver = Get(destination);
            if (receiver != null) ApplyObserverTransfer(receiver);
            else if (destination.transform.parent != null && Observers.TryGetValue(destination, out view)) ApplyObserverTransfer(view.Agent);
        }
        static void ApplyObserverTransfer(Agent receiver)
        {
            List<Transfer> pending; if (!Transfers.TryGetValue(receiver.Owner, out pending)) return;
            Transfers.Remove(receiver.Owner); float now = Time.time; Transfer heard = null; float distance = float.MaxValue;
            foreach (var item in pending)
            {
                if (item.Until <= now || !PlayerAvailable(item.Player)) continue;
                var contact = Contact(receiver, item.Player); contact.WorldPosition = item.World; contact.ContactUntil = item.Until;
                if (item.Seen)
                {
                    contact.Acquired = true; contact.VisibleAt = item.SeenAt; receiver.Crew = true;
                    FoughtUntil(receiver, item.Player, item.Until, "this crew member detected him before changing seats");
                }
                else { float d = (Origin.Scene(item.World) - receiver.T.position).sqrMagnitude; if (d < distance) { heard = item; distance = d; } }
            }
            receiver.NextLook = now;
            if (heard != null && receiver.Detection != null && receiver.SightOwner == null)
            {
                var ghost = GetOrMake(Src.Engine, Origin.Scene(heard.World), heard.Player, "crew heard a player's vehicle", 0f, 1f, now);
                ghost.Subject = heard.Player; Assign(receiver, ghost, Src.Engine, now);
            }
        }
    }
}
