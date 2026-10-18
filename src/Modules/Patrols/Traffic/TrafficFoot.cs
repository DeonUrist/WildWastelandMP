using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WildWasteland.Modules.Patrols
{
    // Foot patrols ([Patrols] Foot patrols, world traffic on): 2-5 walkers on a TrafficJourney (foot = true, vehicles empty) between camps of one
    // faction, over the same land routes the cars use. Virtual (TrafficJourney.Advance) away from the player; within FootPatrolLoadRange they
    // become real simple-bandit NPCs walked by Services.Npc.Travel along the route (journey.Follow the leader), and simulated again beyond
    // FootPatrolUnloadRange. Real walkers are journey-owned: never in the native save lists (the journey is saved in the traffic sidecar).
    // At a loaded destination they become ordinary camp residents (natively saved); unloaded / virtual arrivals just end.
    // Crew left without a usable car (CrewReturn.Stranded) are wrapped into a foot journey to the nearest uncleared camp of their faction.
    // Runs inside WorldTraffic.Tick (0.25 s): per walker work at most 1 Hz, one walker instantiated per tick, no per-tick allocation.
    internal sealed partial class WorldTraffic
    {
        private const int MaxWalkers = 5;
        private sealed class FootLive
        {
            internal readonly GameObject[] Bodies = new GameObject[MaxWalkers];
            internal readonly FsmFloat[] Health = new FsmFloat[MaxWalkers];
            internal readonly bool[] Spawned = new bool[MaxWalkers];
            internal readonly Vector3[] Ordered = new Vector3[MaxWalkers];
            internal readonly int[] Fails = new int[MaxWalkers];
            internal readonly int[] State = new int[MaxWalkers];
            internal float NextOrder = -1f, SettleAt, MinDistance;
            internal int Settle;
            internal bool Stuck;
        }
        private sealed class FootDeparture
        {
            internal RoadJob Job, Back;   // out to the POI, back to the home camp
            internal TrafficBase Source, Destination;
            internal TrafficPoi Point;
            internal float Since;
            internal string Name, Faction;
            internal TrafficWalker[] Walkers;
        }
        private readonly Dictionary<string, FootLive> _footLive = new Dictionary<string, FootLive>();
        private readonly List<CrewReturn.StrandedCrew> _wrap = new List<CrewReturn.StrandedCrew>(MaxWalkers);
        private FootDeparture _footDeparture;
        private float _nextFootSecond, _nextFootRoll;
        private int _footSpawnFrame = -1;
        private static readonly RaycastHit[] FootHits = new RaycastHit[16];
        private static readonly TrafficVehicle[] NoVehicles = new TrafficVehicle[0];
        private static readonly Vector3[][] NoHints = new Vector3[0][];

        private void ResetFoot() { _footLive.Clear(); _footDeparture = null; _nextFootRoll = 0f; _nextFootSecond = 0f; _wrap.Clear(); _settling.Clear(); _settlingUntil.Clear(); }
        // settled residents' last travel order (into the camp): cancelled once it ends, so the NPC Idle camp logic takes them over
        private readonly List<GameObject> _settling = new List<GameObject>();
        private readonly List<float> _settlingUntil = new List<float>();
        // a walker leaves the journey (released, hired, removed): its walk order goes with it
        private static void Unorder(GameObject body)
        { var api = Core.Services.Npc; if (api != null && body != null) try { api.Travel(body, null, 0f); } catch (Exception) { } }

        // module shutdown: live walkers stay in the world as ordinary (natively saved) NPCs
        internal static void ReleaseFootAll() { if (_instance != null) _instance.ReleaseFoot("Patrols off"); }
        private void ReleaseFoot(string why)
        {
            foreach (var fl in _footLive.Values)
                for (int i = 0; i < MaxWalkers; i++) { var body = fl.Bodies[i]; if (body != null) { Register.Add(body, false); Unorder(body); fl.Bodies[i] = null; } }
            if (_footLive.Count > 0) PatrolsModule.Verbose("Foot patrols: real walkers released to the world (" + why + ")");
            _footLive.Clear(); _footDeparture = null;
        }

        private static bool FootValid(TrafficJourney j)
        {
            if (j.walkers == null || j.walkers.Length < 1 || j.walkers.Length > MaxWalkers) return false;
            foreach (var w in j.walkers) if (w == null || string.IsNullOrEmpty(w.identity)) return false;
            return true;
        }
        private static bool AnyWalker(TrafficJourney j) { foreach (var w in j.walkers) if (!w.dead) return true; return false; }
        internal static bool FootAlive(TrafficJourney j) { return j.foot && j.walkers != null && AnyWalker(j); }
        // every foot group (travelers and wrapped stranded crew) is "<Faction> travelers"; the car group name is not used
        internal static string FootName(string faction, string group) { return TrafficFactions.Normalize(faction) == "Coyotes" ? "Coyote travelers" : "Scrapyard travelers"; }

        // the compass/map target of a real foot patrol: its first living walker
        private Transform FootLeader(TrafficJourney journey)
        {
            FootLive fl;
            if (!_footLive.TryGetValue(journey.id, out fl)) return null;
            for (int i = 0; i < MaxWalkers; i++) if (fl.Bodies[i] != null) return fl.Bodies[i].transform;
            return null;
        }

        // ------------------------------------------------------------ per journey (0.25 s)

        private void FootStep(TrafficJourney journey, float seconds, Transform player)
        {
            if (Core.Multiplayer.Hosting) { var nearest = Core.Multiplayer.NearestPlayer(TrafficSpace.Scene(journey.position)); if (nearest != null) player = nearest.transform; }
            var playerWorld = TrafficSpace.World(player.position);
            FootLive fl;
            _footLive.TryGetValue(journey.id, out fl);
            if (fl != null) FootBodies(journey, fl);
            if (!AnyWalker(journey)) { if (Core.Log.Verbose) PatrolsModule.Verbose("Foot patrol " + journey.name + ": everyone died"); Remove(journey); return; }
            if (!journey.arrived && !string.IsNullOrEmpty(journey.destination) && Cleared(journey.destination) && !FootRetarget(journey, fl != null)) return;
            if (fl == null)
            {
                if (journey.arrived) { if (Core.Log.Verbose) PatrolsModule.Verbose("Foot patrol " + journey.name + " arrived while simulated; removed"); Remove(journey); return; }
                journey.Advance(seconds);
                if (Core.Services.Npc != null && TrafficLocations.FlatDistance(journey.position, playerWorld) <= PatrolsModule.FootLoadRange.Value && !Convoy.AutosaveSoon(30f)
                    && (!TrafficFactions.PlayerHostile(journey.faction) || TrafficLocations.PatrolSpawnAllowed(journey.position)))
                {
                    _footLive[journey.id] = new FootLive { NextOrder = Time.time + UnityEngine.Random.Range(0.5f, 1.5f) };
                    if (Core.Log.Verbose) PatrolsModule.Verbose("Foot patrol " + journey.name + " materializes (" + journey.walkers.Length + " walkers, " + TrafficLocations.FlatDistance(journey.position, playerWorld).ToString("0") + " m away)");
                }
                return;
            }
            if (fl.Settle > 0) { if (Time.time >= fl.SettleAt) FootSettle(journey, fl); return; }
            if (Core.Services.Npc == null)
            {   // walkers near the player never vanish in front of him: they stay as ordinary (natively saved) NPCs
                if (fl.MinDistance <= PatrolsModule.FootLoadRange.Value && FootLeader(journey) != null) { PatrolsModule.Verbose("Foot patrol " + journey.name + ": NPC module off, walkers released"); RemoveFoot(journey); }
                else FootVirtualize(journey, fl, "NPC module off");
                return;
            }
            FootSpawnOne(journey, fl);
            if (Time.time >= fl.NextOrder)
            {
                fl.NextOrder += 1f; if (fl.NextOrder < Time.time) fl.NextOrder = Time.time + 1f;
                FootWalk(journey, fl, player);
                float unload = Mathf.Max(PatrolsModule.FootUnloadRange.Value, PatrolsModule.FootLoadRange.Value + 80f);
                bool anyBody = false; for (int i = 0; i < MaxWalkers; i++) if (fl.Bodies[i] != null) { anyBody = true; break; }
                if ((!anyBody || fl.MinDistance > unload) && TrafficLocations.FlatDistance(journey.position, playerWorld) > unload) { FootVirtualize(journey, fl, "far from the player"); return; }
                if (journey.arrived && anyBody && TrafficLocations.Known.TryGetValue(journey.destination ?? "", out var camp) && !camp.cleared && TrafficCamps.Loaded(camp.id))
                    FootBeginSettle(journey, fl);
            }
        }

        // deaths (the game swaps a dead NPC for its carcass) and walkers taken over by something else (a quest hire, a seat)
        private void FootBodies(TrafficJourney journey, FootLive fl)
        {
            for (int i = 0; i < journey.walkers.Length && i < MaxWalkers; i++)
            {
                var w = journey.walkers[i];
                if (!fl.Spawned[i] || w.dead) continue;
                var body = fl.Bodies[i];
                if (body == null || (fl.Health[i] != null && fl.Health[i].Value <= 0f))
                { w.dead = true; w.health = 0f; fl.Bodies[i] = null; if (Core.Log.Verbose) PatrolsModule.Verbose("Foot patrol " + journey.name + ": walker " + (i + 1) + " (" + w.identity + ") died"); continue; }
                if (body.transform.parent != null || QuestFollowers.IsHired(body))
                { w.dead = true; fl.Bodies[i] = null; Register.Add(body, false); Unorder(body); PatrolsModule.Verbose("Foot patrol " + journey.name + ": walker " + body.name + " left the group"); }
            }
        }

        private void FootSpawnOne(TrafficJourney journey, FootLive fl)
        {
            if (_footSpawnFrame == Time.frameCount) return;   // one instantiation per frame across all foot patrols
            for (int i = 0; i < journey.walkers.Length && i < MaxWalkers; i++)
            {
                if (fl.Spawned[i] || journey.walkers[i].dead) continue;
                _footSpawnFrame = Time.frameCount;
                FootSpawn(journey, fl, i);
                return;
            }
        }

        private void FootSpawn(TrafficJourney journey, FootLive fl, int i)
        {
            var w = journey.walkers[i];
            var prefab = Prefabs.FindAny(w.identity);
            if (prefab == null) { w.dead = true; fl.Spawned[i] = true; Core.Log.Warn("[Patrols] Foot patrol: walker prefab not found: " + w.identity); return; }
            float progress = RoadPath.Progress(journey.route, journey.leg, journey.position) - i * 2.5f;
            var dir = FootDirection(journey.route, progress);
            var world = RoadPath.Point(journey.route, progress) + Vector3.Cross(Vector3.up, dir) * (((i & 1) == 0 ? 1f : -1f) * 0.8f);
            Vector3 ground;
            if (!FootGround(TrafficSpace.Scene(world), out ground)) return;   // retried next tick
            var mob = UnityEngine.Object.Instantiate(prefab, ground + Vector3.up * 0.5f, Quaternion.LookRotation(dir, Vector3.up));
            SimpleBanditBridge.Fight(mob);
            TrafficFactions.Actor(mob, journey.faction);
            mob.SetActive(true);
            var col = mob.GetComponent<Collider>();
            if (col != null) mob.transform.position += Vector3.up * (ground.y + 0.15f - col.bounds.min.y);
            Register.Name(mob, prefab.name); CrewAppearance.Dress(mob, w.identity, false);
            Core.Multiplayer.RegisterActor(mob, CrewAppearance.Identity(mob, prefab.name), null);
            if (w.health > 0f) { Patrol.SetHealth(mob, w.health); mob.AddComponent<Patrol.LateHealth>().Value = w.health; }
            fl.Bodies[i] = mob; fl.Health[i] = CrewReturn.HealthVar(mob); fl.Spawned[i] = true; fl.Fails[i] = 0; fl.Ordered[i] = new Vector3(float.MaxValue, 0f, 0f);
            if (Core.Log.Verbose) PatrolsModule.Verbose("Foot patrol " + journey.name + ": walker " + mob.name + " spawned at " + mob.transform.position);
        }

        // 1 Hz: formation targets ahead along the route; the journey follows the leader. A leader that cannot get anywhere (Travel failed
        // repeatedly) lets the journey advance virtually, and walkers out of the player's sight catch up by moving them.
        private void FootWalk(TrafficJourney journey, FootLive fl, Transform player)
        {
            var api = Core.Services.Npc;
            int leader = -1;
            for (int i = 0; i < MaxWalkers; i++) if (fl.Bodies[i] != null) { leader = i; break; }
            fl.MinDistance = float.MaxValue;
            if (leader < 0) return;
            bool near = false;
            for (int i = 0; i < MaxWalkers; i++)
            {
                var body = fl.Bodies[i]; if (body == null) continue;
                float d = Vector3.Distance(body.transform.position, player.position);
                if (d < fl.MinDistance) fl.MinDistance = d;
                if (d <= 100f) near = true;
                int state;
                try { state = api.TravelState(body); } catch (Exception) { state = 3; }
                fl.State[i] = state;
                if (state == 3) fl.Fails[i]++; else if (state == 1 || state == 2) fl.Fails[i] = 0;
            }
            // sticky until the leader walks again (state 1 / 2), so moved walkers do not make the journey jump ahead
            if (fl.State[leader] == 1 || fl.State[leader] == 2) fl.Stuck = false; else if (fl.Fails[leader] >= 4) fl.Stuck = true;
            if (!fl.Stuck) journey.Follow(TrafficSpace.World(fl.Bodies[leader].transform.position), journey.leg >= journey.route.Length - 1 ? 8f : 15f);
            else if (!near) journey.Advance(1f);
            float progress = RoadPath.Progress(journey.route, journey.leg, journey.position);
            int k = 0;
            for (int i = 0; i < MaxWalkers; i++)
            {
                var body = fl.Bodies[i]; if (body == null) continue;
                float at = journey.arrived ? float.MaxValue : progress + 14f - k * 2.5f;
                var end = journey.route[journey.route.Length - 1];
                var dir = FootDirection(journey.route, journey.arrived ? progress : at);
                var world = (journey.arrived ? end : RoadPath.Point(journey.route, at)) + Vector3.Cross(Vector3.up, dir) * (((k & 1) == 0 ? 1f : -1f) * ((k + 1) / 2) * 1.3f);
                var scene = TrafficSpace.Scene(world);
                int state = fl.State[i];
                if (fl.Stuck && !near && fl.Fails[i] > 0)
                {
                    Vector3 ground;
                    if (FootGround(scene, out ground)) { body.transform.position = ground + Vector3.up * 0.1f; fl.Fails[i] = 0; state = 0; }
                }
                if ((state != 1 && state != 2) || (scene - fl.Ordered[i]).sqrMagnitude > 36f)
                {
                    fl.Ordered[i] = scene;
                    try { api.Travel(body, scene, 3f); } catch (Exception e) { PatrolsModule.Verbose("Foot patrol travel: " + e.Message); }
                }
                k++;
            }
        }

        private static Vector3 FootDirection(Vector3[] route, float progress)
        {
            var d = RoadPath.Point(route, progress + 2f) - RoadPath.Point(route, progress - 2f); d.y = 0f;
            return d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward;
        }

        private static bool FootGround(Vector3 p, out Vector3 ground)
        {
            ground = p;
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 400f, p.z), Vector3.down, FootHits, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool ok = false;
            for (int i = 0; i < n; i++)
            {
                var h = FootHits[i];
                if (h.collider == null || h.collider.attachedRigidbody != null || h.distance >= best) continue;   // not on a car or a body
                best = h.distance; ground = h.point; ok = h.normal.y > 0.6f;
            }
            return ok;
        }

        // simulated again: health captured, bodies removed (journey-owned, never in the native lists)
        private void FootVirtualize(TrafficJourney journey, FootLive fl, string why)
        {
            for (int i = 0; i < MaxWalkers && i < journey.walkers.Length; i++)
            {
                var body = fl.Bodies[i]; if (body == null) continue;
                if (fl.Health[i] != null) journey.walkers[i].health = fl.Health[i].Value;
                fl.Bodies[i] = null; UnityEngine.Object.Destroy(body);
            }
            _footLive.Remove(journey.id);
            if (Core.Log.Verbose) PatrolsModule.Verbose("Foot patrol " + journey.name + " simulated again (" + why + ")");
            if (journey.arrived) Remove(journey);
        }

        // arrival at a loaded camp: step 1 drops the simple-bandit / faction-actor components (ordinary NPC AI again)...
        private void FootBeginSettle(TrafficJourney journey, FootLive fl)
        {
            var api = Core.Services.Npc;
            for (int i = 0; i < MaxWalkers; i++)
            {
                var body = fl.Bodies[i]; if (body == null) continue;
                try { if (api != null) api.Travel(body, null, 0f); } catch (Exception) { }
                QuestFollowers.FormerCrew(body);   // no crew tags from now on: the quest restrictions still know it
                var tag = body.GetComponent<SimpleBanditTag>(); if (tag != null) UnityEngine.Object.Destroy(tag);
                var actor = body.GetComponent<TrafficActor>(); if (actor != null) UnityEngine.Object.Destroy(actor);
            }
            fl.Settle = 1; fl.SettleAt = Time.time + 0.5f;
        }

        // ...step 2 (a tick later): natively saved residents of the camp, sent inside so the NPC Idle picks a spawn point there
        private void FootSettle(TrafficJourney journey, FootLive fl)
        {
            TrafficBase camp;
            TrafficLocations.Known.TryGetValue(journey.destination ?? "", out camp);
            var api = Core.Services.Npc;
            int n = 0;
            for (int i = 0; i < MaxWalkers; i++)
            {
                var body = fl.Bodies[i]; if (body == null) continue;
                fl.Bodies[i] = null; n++;
                Register.Add(body, false);
                if (camp != null)
                {
                    TrafficCamps.Bind(camp.id, body);
                    try { if (api != null) { api.Travel(body, TrafficSpace.Scene(camp.center), 4f); _settling.Add(body); _settlingUntil.Add(Time.time + 120f); } } catch (Exception) { }
                }
            }
            _footLive.Remove(journey.id); _journeys.Remove(journey);
            if (Core.Log.Verbose) PatrolsModule.Verbose("Foot patrol " + journey.name + " arrived at " + (camp != null ? camp.name + " " + camp.id : "its camp") + ": " + n + " new resident(s)");
        }

        private void RemoveFoot(TrafficJourney journey)
        {
            FootLive fl;
            if (_footLive.TryGetValue(journey.id, out fl))
                for (int i = 0; i < MaxWalkers; i++) { var body = fl.Bodies[i]; if (body != null) { fl.Bodies[i] = null; Register.Add(body, false); Unorder(body); } }   // never vanish in front of the player
            _footLive.Remove(journey.id);
            _journeys.Remove(journey);
        }

        // the destination was cleared on the way: the nearest uncleared camp of the faction, else stop (removed once simulated)
        private bool FootRetarget(TrafficJourney journey, bool live)
        {
            var camp = NearestCamp(journey.faction, journey.position, journey.destination);
            if (camp == null)
            {
                if (!live) { Remove(journey); return false; }
                journey.destination = ""; journey.route = new[] { journey.position, journey.position }; journey.roadLegs = new bool[2]; journey.leg = 1; journey.arrived = true;
                return true;
            }
            bool[] roads;
            var route = RoadPlanner.Build(journey.position, camp.apron, NoHints, TrafficLocations.Blocks(), 0f, journey.id.GetHashCode(), out roads);
            if (route == null || route.Length < 2)
            {
                if (!live) { Remove(journey); return false; }
                journey.destination = ""; journey.route = new[] { journey.position, journey.position }; journey.roadLegs = new bool[2]; journey.leg = 1; journey.arrived = true;
                return true;
            }
            journey.destination = camp.id; journey.route = route; journey.roadLegs = roads; journey.leg = 1; journey.arrived = false;
            PatrolsModule.Verbose("Foot patrol " + journey.name + " heads for " + camp.name + " instead (destination cleared)");
            return true;
        }

        private static TrafficBase NearestCamp(string faction, Vector3 world, string except)
        {
            faction = TrafficFactions.Normalize(faction);
            TrafficBase best = null; float distance = float.MaxValue;
            foreach (var camp in TrafficLocations.Known.Values)
            {
                if (camp.cleared || camp.id == except || TrafficFactions.Normalize(camp.faction) != faction) continue;
                float d = TrafficLocations.FlatDistance(camp.apron, world);
                if (d < distance) { distance = d; best = camp; }
            }
            return best;
        }

        // ------------------------------------------------------------ crew left without a usable car (1 Hz)

        private void StrandedTick(float seconds)
        {
            if (_settling.Count > 0) SettlingTick();
            var list = CrewReturn.Stranded;
            if (list.Count == 0) return;
            var api = Core.Services.Npc;
            if (!PatrolsModule.FootPatrols.Value || api == null) { list.Clear(); return; }   // they stay ordinary NPCs, saved natively
            float wait = PatrolsModule.StrandedIdleSeconds.Value;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var s = list[i];
                if (s.Body == null || (s.Health != null && s.Health.Value <= 0f) || s.Body.transform.parent != null || QuestFollowers.IsHired(s.Body)) { list.RemoveAt(i); continue; }
                bool idle; try { idle = api.IsIdle(s.Body); } catch (Exception) { idle = false; }
                s.Idle = idle ? s.Idle + seconds : 0f;
            }
            if (_journeys.Count >= PatrolsModule.TrafficMaxGroups.Value) return;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s.Idle < wait) continue;
                _wrap.Clear(); _wrap.Add(s);
                var at = s.Body.transform.position;
                for (int j = 0; j < list.Count && _wrap.Count < MaxWalkers; j++)
                {
                    var o = list[j];
                    if (o == s || o.Idle < wait * 0.5f || o.Faction != s.Faction || (o.Body.transform.position - at).sqrMagnitude > 60f * 60f) continue;
                    _wrap.Add(o);
                }
                for (int j = 0; j < _wrap.Count; j++) list.Remove(_wrap[j]);
                WrapStranded(_wrap);
                _wrap.Clear();
                return;   // one group per second
            }
        }

        private void SettlingTick()
        {
            var api = Core.Services.Npc; float now = Time.time;
            for (int i = _settling.Count - 1; i >= 0; i--)
            {
                var body = _settling[i];
                if (body != null && api != null && now < _settlingUntil[i])
                { int state; try { state = api.TravelState(body); } catch (Exception) { state = 3; } if (state == 1) continue; }
                Unorder(body);
                _settling.RemoveAt(i); _settlingUntil.RemoveAt(i);
            }
        }

        private void WrapStranded(List<CrewReturn.StrandedCrew> group)
        {
            var first = group[0];
            var start = TrafficSpace.World(first.Body.transform.position);
            var camp = NearestCamp(first.Faction, start, null);
            if (camp == null) { if (Core.Log.Verbose) PatrolsModule.Verbose("Foot patrols: no " + first.Faction + " camp known; " + group.Count + " stranded crew stay where they are"); return; }
            bool[] roads;
            var route = RoadPlanner.Build(start, camp.apron, NoHints, TrafficLocations.Blocks(), 0f, unchecked(first.Body.GetInstanceID() * 397), out roads);
            if (route == null || route.Length < 2) return;
            var walkers = new TrafficWalker[group.Count];
            var fl = new FootLive { NextOrder = Time.time + UnityEngine.Random.Range(0.2f, 1f) };
            var api = Core.Services.Npc;
            for (int k = 0; k < group.Count; k++)
            {
                var s = group[k];
                walkers[k] = new TrafficWalker { identity = string.IsNullOrEmpty(s.Identity) ? CrewAppearance.Identity(s.Body, TemplateExporter.PrefabName(s.Body.name)) : s.Identity, health = s.Health != null ? s.Health.Value : -1f };
                fl.Bodies[k] = s.Body; fl.Health[k] = s.Health; fl.Spawned[k] = true; fl.Ordered[k] = new Vector3(float.MaxValue, 0f, 0f);
                Register.RemoveTree(s.Body);   // journey-owned: the traffic sidecar saves them now
                try { if (api != null) api.Travel(s.Body, null, 0f); } catch (Exception) { }
            }
            var journey = new TrafficJourney { id = Guid.NewGuid().ToString("N"), name = FootName(first.Faction, first.Name), faction = first.Faction, source = "", destination = camp.id,
                foot = true, route = route, roadLegs = roads, leg = 1, position = route[0], speed = PatrolsModule.FootSpeed.Value, walkers = walkers, vehicles = NoVehicles };
            _journeys.Add(journey); _footLive[journey.id] = fl;
            if (Core.Log.Verbose) PatrolsModule.Verbose("Foot patrols: " + group.Count + " crew without a car (" + journey.name + ") walk to " + camp.name + " " + camp.id + ", " + TrafficLocations.FlatDistance(start, camp.apron).ToString("0") + " m");
        }

        // ------------------------------------------------------------ population (1 Hz, with the vehicle groups)

        private const float FootRetrySeconds = 10f;
        private string _footWhy;
        private void FootRetry(string why)
        {
            _nextFootRoll = Time.time + FootRetrySeconds;
            if (Core.Log.Verbose && why != _footWhy) PatrolsModule.Verbose("Foot patrols: none can leave yet - " + why + " (retry in " + FootRetrySeconds.ToString("0") + " s)");
            _footWhy = why; _footStateCode = 6;
        }
        // diagnostics: every reason a foot patrol is not leaving is logged once when it changes (verbose only, no per-tick strings)
        private int _footStateCode = -1;
        private void FootState(int code, string text)
        {
            if (code == _footStateCode) return;
            _footStateCode = code;
            if (Core.Log.Verbose) PatrolsModule.Verbose("Foot patrols: " + text);
        }
        // one MapMagic main tile ("chunk"); 5 km when the map is not known yet
        private static float ChunkMetres() { var size = TrafficRoads.TileSize; float c = Mathf.Max(size.x, size.z); return c > 0f ? c : 5000f; }
        private static bool InsideCamp(Vector3 world)
        {
            foreach (var c in TrafficLocations.Known.Values)
                if (Mathf.Abs(world.x - c.center.x) <= c.size.x / 2f + 40f && Mathf.Abs(world.z - c.center.z) <= c.size.z / 2f + 40f) return true;
            return false;
        }
        private void PopulateFoot(Vector3 playerWorld)
        {
            if (!PatrolsModule.FootPatrols.Value) { _footDeparture = null; FootState(1, "off (Game > World > Foot patrols)"); return; }
            if (Core.Services.Npc == null) { _footDeparture = null; FootState(2, "waiting - the NPC module is not running"); return; }
            int count = 0;
            for (int i = 0; i < _journeys.Count; i++) if (_journeys[i].foot) count++;
            if (count >= PatrolsModule.FootMaxActive.Value) { _footDeparture = null; FootState(3, count + " active = FootPatrolMaxActive"); return; }
            if (_journeys.Count >= PatrolsModule.TrafficMaxGroups.Value) { _footDeparture = null; FootState(4, "world traffic is full (" + _journeys.Count + " groups = MaxGroups)"); return; }
            if (_footDeparture != null)
            {
                var pending = _footDeparture;
                if (!pending.Job.Complete || !pending.Back.Complete)
                {
                    if (Time.time - pending.Since > 45f) { _footDeparture = null; FootRetry("the route planner did not answer in 45 s"); return; }
                    FootState(5, "routing " + pending.Source.name + " -> " + pending.Point.name + " and back"); return;
                }
                _footDeparture = null;
                var outward = pending.Job.Route; var back = pending.Back.Route;
                if (outward == null || outward.Length < 2 || back == null || back.Length < 2 || Cleared(pending.Source.id) || TrafficLocations.FlatDistance(pending.Source.center, TrafficSpace.World(PlayerRef.Player != null ? PlayerRef.Player.position : Vector3.zero)) < PatrolsModule.FootMinSourceDistance.Value)
                { FootRetry(outward == null || outward.Length < 2 || back == null || back.Length < 2 ? "no land route " + pending.Source.name + " <-> " + pending.Point.name : "you came too close to the home camp, or it was cleared, while routing"); return; }
                // one route: home camp -> far POI -> home camp (the roaming Coyote precedent: two planner jobs joined at the POI)
                var route = new Vector3[outward.Length + back.Length - 1];
                Array.Copy(outward, route, outward.Length); Array.Copy(back, 1, route, outward.Length, back.Length - 1);
                var roads = new bool[route.Length];
                if (pending.Job.Roads != null) Array.Copy(pending.Job.Roads, roads, Mathf.Min(pending.Job.Roads.Length, outward.Length));
                if (pending.Back.Roads != null && pending.Back.Roads.Length > 1) Array.Copy(pending.Back.Roads, 1, roads, outward.Length, Mathf.Min(pending.Back.Roads.Length - 1, back.Length - 1));
                var journey = new TrafficJourney { id = Guid.NewGuid().ToString("N"), name = pending.Name, faction = pending.Faction, source = pending.Source.id, destination = pending.Source.id,
                    foot = true, route = route, roadLegs = roads, TerrainRoute = pending.Job.Elevation || pending.Back.Elevation, leg = 1, position = route[0],
                    speed = PatrolsModule.FootSpeed.Value, walkers = pending.Walkers, vehicles = NoVehicles };
                _journeys.Add(journey);
                _footStateCode = -1;
                if (Core.Log.Verbose) PatrolsModule.Verbose("Foot patrol " + journey.name + " (" + journey.walkers.Length + " walkers) leaves " + pending.Source.name + " for " + pending.Point.name + " (" +
                    TrafficLocations.FlatDistance(pending.Source.apron, pending.Point.position).ToString("0") + " m) and back");
                return;
            }
            if (Time.time < _nextFootRoll) return;
            // the chance roll only paces extra groups: with none on the map one is tried at once, and an attempt that cannot
            // start (no group / camp / route) retries after FootRetrySeconds instead of waiting a whole check interval
            if (count > 0 && UnityEngine.Random.value * 100f >= PatrolsModule.FootSpawnChance.Value) { _nextFootRoll = Time.time + PatrolsModule.FootCheckSeconds.Value; return; }
            // Travelers: no editor groups and no progression gates. A random faction (Scrapyard / Coyotes, the other one when it has
            // no camp to leave from), 2-5 people rolled at random from that faction's NPC types, named "<Faction> travelers".
            // Walkers keep their crew tags, so the Quests bridge lets the player hire them on foot but never gives them quests.
            float range = PatrolsModule.CampTrafficRange.Value, longest = ChunkMetres(), minSource = PatrolsModule.FootMinSourceDistance.Value;
            string faction = UnityEngine.Random.value < 0.5f ? "Coyotes" : "Scrapyard";
            var sources = new List<TrafficBase>();
            for (int attempt = 0; attempt < 2 && sources.Count == 0; attempt++)
            {
                if (attempt == 1) faction = faction == "Coyotes" ? "Scrapyard" : "Coyotes";
                foreach (var camp in TrafficLocations.Known.Values)
                { float d = TrafficLocations.FlatDistance(camp.center, playerWorld); if (!camp.cleared && TrafficFactions.Normalize(camp.faction) == faction && TrafficCamps.Loaded(camp.id) && d <= range && d >= minSource) sources.Add(camp); }   // the same camps car patrols leave from, but out of sight: they walk simulated until they come near
            }
            if (sources.Count == 0)
            {
                int known = 0, loaded = 0, near = 0;
                foreach (var camp in TrafficLocations.Known.Values)
                {
                    if (camp.cleared) continue;
                    known++; if (!TrafficCamps.Loaded(camp.id)) continue; loaded++;
                    if (TrafficLocations.FlatDistance(camp.center, playerWorld) < minSource) near++;
                }
                FootRetry("no loaded uncleared Scrapyard or Coyote camp " + minSource.ToString("0") + "-" + range.ToString("0") + " m away (" + known + " known, " + loaded + " loaded, " + near + " too close)");
                return;
            }
            var source = sources[UnityEngine.Random.Range(0, sources.Count)];
            // the walk: out to a far point of interest and back home, anywhere within one MapMagic chunk (main tile) of the camp.
            // Turn-around candidates, best first: a location POI (building / wreck / cave / starter area outside every camp) at least FootPatrolMinPoiMetres
            // away, any such POI, another raider camp's apron, then a plain point on the land (the route planner keeps it on terrain)
            var targets = new List<TrafficPoi>(); TrafficPoi farthest = null; float farthestD = 0f, nearest = PatrolsModule.FootMinPoiMetres.Value;
            int inCamp = 0, tooNear = 0, tooFar = 0;
            foreach (var poi in TrafficLocations.Points.Values)
            {
                if (poi == null) continue;
                if (InsideCamp(poi.position)) { inCamp++; continue; }
                float d = TrafficLocations.FlatDistance(source.apron, poi.position);
                if (d < 300f) { tooNear++; continue; } if (d > longest) { tooFar++; continue; }
                if (d >= nearest) targets.Add(poi); else if (d > farthestD) { farthestD = d; farthest = poi; }
            }
            if (targets.Count == 0 && farthest != null) targets.Add(farthest);
            string kind = "POI";
            if (targets.Count == 0)
                foreach (var camp in TrafficLocations.Known.Values)
                {
                    if (camp == source || camp.cleared || TrafficFactions.Normalize(camp.faction) != faction) continue;   // a friendly camp only
                    float d = TrafficLocations.FlatDistance(source.apron, camp.apron);
                    if (d >= nearest && d <= longest) { targets.Add(new TrafficPoi { id = camp.id, name = camp.name, position = camp.apron }); kind = "camp"; }
                }
            if (targets.Count == 0)
            {
                // open land: a random bearing, FootPatrolMinPoiMetres .. half a chunk out (simulated until near the player; walkers are grounded on spawn)
                float a = UnityEngine.Random.value * Mathf.PI * 2f, dist = UnityEngine.Random.Range(Mathf.Min(nearest, longest * 0.5f), longest * 0.5f);
                var at = source.apron + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * dist;
                if (!InsideCamp(at)) { targets.Add(new TrafficPoi { id = "", name = "the wasteland", position = at }); kind = "land"; }
            }
            if (targets.Count == 0) { FootRetry("no turn-around point within one chunk (" + longest.ToString("0") + " m) of " + source.name + " - POIs: " + TrafficLocations.Points.Count + " known, " + inCamp + " in camps, " + tooNear + " too near, " + tooFar + " too far"); return; }
            var point = targets[UnityEngine.Random.Range(0, targets.Count)];
            if (Core.Log.Verbose && kind != "POI") PatrolsModule.Verbose("Foot patrols: no far POI from " + source.name + " (" + TrafficLocations.Points.Count + " known, " + inCamp + " in camps, " + tooNear + " too near, " + tooFar + " too far) - turns around at " + (kind == "camp" ? point.name : "open land"));
            var pool = CrewFactions.Travelers(faction);
            var walkers = new TrafficWalker[UnityEngine.Random.Range(2, MaxWalkers + 1)];
            for (int i = 0; i < walkers.Length; i++) walkers[i] = new TrafficWalker { identity = pool[UnityEngine.Random.Range(0, pool.Length)] };
            _nextFootRoll = Time.time + PatrolsModule.FootCheckSeconds.Value; _footWhy = null;
            var stop = new TrafficBase { id = point.id, name = point.name, apron = point.position, center = point.position, gate = point.position };
            _footDeparture = new FootDeparture { Job = RoadPlanner.RequestRoute(source, stop, false, true), Back = RoadPlanner.RequestRoute(stop, source, false, true), Source = source, Destination = source, Point = point, Since = Time.time,
                Name = FootName(faction, null), Faction = faction, Walkers = walkers };
        }

        // saving: health / deaths of real walkers into the journey record
        private void CaptureFoot()
        {
            foreach (var journey in _journeys)
            {
                FootLive fl;
                if (!journey.foot || !_footLive.TryGetValue(journey.id, out fl)) continue;
                FootBodies(journey, fl);
                for (int i = 0; i < MaxWalkers && i < journey.walkers.Length; i++) if (fl.Bodies[i] != null && fl.Health[i] != null) journey.walkers[i].health = fl.Health[i].Value;
            }
        }
    }
}
