using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WildWasteland.Modules.Patrols
{
    // Dismounted crew (Patrol.BailOut, RiderGuard.SpawnMob / Land):
    //  - awareness handover: crew that shot at / detected / was hit by the player from the car remember him on foot (Services.Npc.Remember);
    //  - return to car ([Patrols] Return to car): membership is recorded per car; once every living member is idle they walk back
    //    (Services.Npc.Travel) and are seated again in their original order with the same identity and health; a passenger (else a gunner)
    //    takes the wheel when the driver is dead. The car is held, not retired / cleaned up / blown up, until everyone is aboard or it gives up;
    //  - crew left without a usable car go to Stranded; WorldTraffic wraps them into a foot patrol after StrandedIdleSeconds out of combat.
    // Runtime only: nothing here is saved. After a reload dismounted crew are ordinary natively saved NPCs again (the behaviour before).
    // Polled by the car's Crew.Update at 2 Hz (per-car random phase) while a record exists; no per-frame work, no allocation per tick.
    internal static class CrewReturn
    {
        internal const int RoleDriver = 0, RolePassenger = 1, RoleTurret = 2;
        private const int OnFoot = 0, Walking = 1, Aboard = 2, Left = 3;
        private const float ArriveRadius = 2.75f, ArrivedFlat = 3.5f, GiveUpSeconds = 900f;

        internal sealed class Member
        {
            internal GameObject Body;
            internal FsmFloat Health;
            internal string Identity, Who;
            internal int Role, Slot, Order, State, Fails;
            internal float[] LocalPos;
            internal bool Standing, Arrived, Excluded, BoardKnown;
            internal Vector3 Board, OrderedPoint;   // Board: car-local boarding point
            internal float OrderedAt;
        }
        internal sealed class Group
        {
            internal GameObject Car;
            internal PatrolMarker Marker;
            internal Crew Crew;
            internal readonly List<Member> Members = new List<Member>(4);
            internal Member DriverMember;
            internal bool Boarding, SeatedDriver, Ejected;
            internal float Created, BoardStart, CalmSince = -1f, LastTick = -1f;   // CalmSince: every member idle since (-1 = not calm)
            internal string TrafficId, Faction, Name;
            internal int TrafficSlot;
        }
        internal sealed class StrandedCrew
        {
            internal GameObject Body;
            internal FsmFloat Health;
            internal string Identity, Faction, Name;
            internal float Idle;
        }

        private static readonly List<Group> Groups = new List<Group>();
        internal static readonly List<StrandedCrew> Stranded = new List<StrandedCrew>();
        private static readonly List<PlayMakerFSM> Fs = new List<PlayMakerFSM>();

        internal static void Reset() { Groups.Clear(); Stranded.Clear(); }

        // world traffic / cleanup / self-destruct leave a car alone while its crew may still come back
        internal static bool Holds(GameObject car)
        {
            if (Groups.Count == 0 || ReferenceEquals(car, null)) return false;
            for (int i = 0; i < Groups.Count; i++) if (ReferenceEquals(Groups[i].Car, car)) return true;
            return false;
        }

        // ------------------------------------------------------------ dismount hooks

        // car = the vehicle carrying the PatrolMarker (a trailer turret passes the truck); role/slot/localPos/standing describe the seat left
        internal static void Dismounted(GameObject car, GameObject mob, int role, int slot, string identity, string who, float[] localPos, bool standing, GameObject source = null)
        {
            if (mob == null || car == null || !PatrolsModule.Running) return;
            var marker = car.GetComponent<PatrolMarker>();
            if (marker == null) return;
            Handover(car, marker, mob, source);
            if (Native(car, marker)) return;
            var g = Find(car);
            if (g == null && Regroupable(car, marker)) g = Create(car, marker);
            string why = g != null ? Unusable(g) : "no return to this car";
            if (g == null || why != null)
            {
                if (Core.Log.Verbose) PatrolsModule.Verbose("Return to car: " + mob.name + " stays on foot (" + why + ")");
                Strand(mob, identity, marker.Faction, NameOf(marker));
                return;
            }
            var m = new Member { Body = mob, Health = HealthVar(mob), Identity = identity, Who = who, Role = role, Slot = slot, LocalPos = localPos, Standing = standing,
                Order = role == RoleDriver ? 0 : role == RolePassenger ? 1 : 2 + Mathf.Max(0, slot) };
            int at = g.Members.Count; while (at > 0 && g.Members[at - 1].Order > m.Order) at--;
            g.Members.Insert(at, m); g.CalmSince = -1f;
            if (Core.Log.Verbose) PatrolsModule.Verbose("Return to car: " + mob.name + " (" + (role == RoleDriver ? "driver" : role == RolePassenger ? "passenger" : "turret " + (slot + 1)) + ", " + identity + ") left " + car.name + "; recorded for the way back");
        }

        // Crew.OnStuck: the crew is about to bail after the doors open; keep the car out of world-traffic retirement in between
        internal static void Expect(Crew crew, GameObject car, PatrolMarker marker)
        {
            if (car == null || marker == null || !PatrolsModule.Running || Native(car, marker) || Find(car) != null) return;
            if (Regroupable(car, marker)) Create(car, marker);
        }

        // the car (and its Crew) was destroyed: whoever is still out becomes stranded
        internal static void CarLost(Group g) { if (g != null && Groups.Contains(g)) { g.Crew = null; End(g, "car gone", false); } }   // the Crew is being destroyed: nothing to finish on it

        // NPC module: crew that engaged the player from the car keep him in mind on foot
        private static void Handover(GameObject car, PatrolMarker marker, GameObject mob, GameObject source)
        {
            var api = Core.Services.Npc; var player = PlayerRef.Player;
            if(Core.Multiplayer.Hosting)
            {
                if(api!=null&&TrafficFactions.PlayerHostile(marker.Faction))api.TransferPlayerAwareness(source??mob,mob,PatrolsModule.DismountMemorySeconds.Value);
                return;
            }
            if (api == null || player == null || PlayerRef.GodMode || !TrafficFactions.PlayerHostile(marker.Faction)) return;
            float memory = PatrolsModule.DismountMemorySeconds.Value; if (memory <= 0f) return;
            bool aware = Time.time - marker.PlayerContactAt <= memory;
            if (!aware) { var agent = WorldTraffic.AgentOf(car); aware = agent != null && agent.SeenUntil > Time.time; }
            if (!aware) return;
            try { api.Remember(mob, player.gameObject, memory); }
            catch (System.Exception e) { PatrolsModule.Verbose("Awareness handover: " + e.Message); return; }
            if (Core.Log.Verbose) PatrolsModule.Verbose("Awareness: " + mob.name + " got out of " + car.name + " knowing where the player is (" + memory.ToString("0") + " s)");
        }

        // ------------------------------------------------------------ eligibility

        // cars whose crews are managed elsewhere (parked setup actors, merchants and their guards, faction battles): nothing changes for them
        private static bool Native(GameObject car, PatrolMarker marker)
        { return marker.SetupOnly || car.GetComponent<MerchantHost>() != null || WorldTraffic.IsBattleCar(car); }

        private static bool Regroupable(GameObject car, PatrolMarker marker)
        {
            return PatrolsModule.ReturnToCar.Value && Core.Services.Npc != null && car.GetComponent<Crew>() != null
                && !EventBudget.IsConvoy(car) && marker.StuckBails == 0;   // a car left stuck is no ride home: its crew become a foot patrol
        }

        private static Group Create(GameObject car, PatrolMarker marker)
        {
            var g = new Group { Car = car, Marker = marker, Crew = car.GetComponent<Crew>(), Created = Time.time,
                TrafficId = marker.TrafficId, TrafficSlot = marker.TrafficSlot, Faction = marker.Faction, Name = NameOf(marker) };
            if (Unusable(g) != null) return null;
            Groups.Add(g); g.Crew.Regroup = g;
            return g;
        }

        private static Group Find(GameObject car)
        {
            for (int i = 0; i < Groups.Count; i++) if (ReferenceEquals(Groups[i].Car, car)) return Groups[i];
            return null;
        }

        private static string NameOf(PatrolMarker marker)
        {
            var journey = !string.IsNullOrEmpty(marker.TrafficId) ? WorldTraffic.Find(marker.TrafficId) : null;
            return journey != null && !string.IsNullOrEmpty(journey.name) ? journey.name : "crew";
        }

        // null = still usable; else why the car cannot take its crew back
        private static string Unusable(Group g)
        {
            var car = g.Car; var marker = g.Marker; var crew = g.Crew;
            if (car == null) return "car gone";
            if (!car.activeInHierarchy) return "car unloaded";
            if (!PatrolsModule.ReturnToCar.Value) return "return to car off";
            if (Core.Services.Npc == null) return "NPC module off";
            if (marker == null || marker.Exploded) return "car exploded";
            if (marker.PlayerEntered || ReferenceEquals(PlayerRef.PlayerCar, car)) return "the player took the car";
            if (crew == null) return "no crew";
            if (crew.OutOfFuel) return "out of fuel";
            if (marker.StuckBails > 0) return "car stuck";
            if (car.transform.up.y < 0.35f) return "car flipped";
            if (Explode.IsWreck(car)) return "car wrecked";
            if (Time.time - g.Created > GiveUpSeconds) return "waited too long";
            return null;
        }

        // ------------------------------------------------------------ the 2 Hz poll (Crew.Update)

        internal static void Tick(Crew crew)
        {
            var g = crew.Regroup; if (g == null) return;
            string why = Unusable(g);
            if (why != null) { End(g, why, false); return; }
            var api = Core.Services.Npc;
            float now = Time.time;
            int pending = 0; bool allIdle = true;
            for (int i = 0; i < g.Members.Count; i++)
            {
                var m = g.Members[i];
                if (m.State == Aboard || m.State == Left) continue;
                if (m.Body == null || (m.Health != null && m.Health.Value <= 0f)) { Leave(g, m, "died"); continue; }
                if (m.Body.transform.parent != null || QuestFollowers.IsHired(m.Body)) { m.Excluded = true; Leave(g, m, "taken by a quest or mounted elsewhere"); continue; }
                pending++;
                if (allIdle && !Idle(api, m.Body)) allIdle = false;
            }
            if (!g.Boarding)
            {
                if (pending == 0) { if (g.Members.Count > 0 || now - g.Created > 5f) End(g, "nobody left to bring back", true); return; }
                if (!allIdle) { g.CalmSince = -1f; return; }   // they fight first
                // calm for 3 s: a fresh dismount's senses have registered and looked once (a remembered player is engaged first, not walked away from)
                if (g.CalmSince < 0f) g.CalmSince = now;
                if (now - g.CalmSince < 3f) return;
                g.Boarding = true; g.BoardStart = now; crew.Regrouping = true;
                if (!crew.DriverSeatedAlive && !g.Ejected) { g.Ejected = true; Patrol.EjectCorpses(g.Car, null); }   // a carcass on the driver seat goes out first
                if (Core.Log.Verbose) PatrolsModule.Verbose("Return to car: fight over, " + pending + " crew walk back to " + g.Car.name);
            }
            if (g.SeatedDriver && !crew.DriverSeatedAlive) { End(g, "the new driver died", true); return; }
            bool needDriver = !crew.DriverSeatedAlive;
            if (needDriver && (g.DriverMember == null || g.DriverMember.State == Left || g.DriverMember.State == Aboard))
            {
                g.DriverMember = null;
                for (int i = 0; i < g.Members.Count; i++) { var m = g.Members[i]; if (m.State == OnFoot || m.State == Walking) { g.DriverMember = m; m.BoardKnown = false; break; } }
                if (g.DriverMember == null) { End(g, "no living crew to drive", true); return; }
                if (Core.Log.Verbose) PatrolsModule.Verbose("Return to car: " + g.DriverMember.Body.name + " will take the wheel of " + g.Car.name);
            }
            if (!allIdle && g.LastTick > 0f) g.BoardStart = Mathf.Min(now, g.BoardStart + (now - g.LastTick));   // the walk-back timeout pauses while they fight
            g.LastTick = now;
            bool timeout = now - g.BoardStart > PatrolsModule.ReturnTimeoutSeconds.Value;
            float maxDistance = PatrolsModule.ReturnMaxDistance.Value;
            var carPos = g.Car.transform.position;
            for (int i = 0; i < g.Members.Count; i++)
            {
                var m = g.Members[i];
                if (m.State != OnFoot && m.State != Walking) continue;
                if (timeout) { Leave(g, m, "took too long"); continue; }
                var point = BoardPoint(g, m);
                if (m.State == OnFoot)
                {
                    if (!Idle(api, m.Body)) continue;
                    if (TrafficLocations.FlatDistance(m.Body.transform.position, carPos) > maxDistance) { Leave(g, m, "too far from the car"); continue; }
                    m.State = Walking; Order(api, m, point, now); continue;
                }
                int state = TravelState(api, m.Body);
                if (state == 2 || TrafficLocations.FlatDistance(m.Body.transform.position, point) <= ArrivedFlat) { m.Arrived = true; continue; }
                m.Arrived = false;
                if (state == 3) { if (++m.Fails >= 3) { Leave(g, m, "cannot reach the car"); continue; } Order(api, m, point, now); }
                else if ((state == 0 && now - m.OrderedAt > 3f) || (point - m.OrderedPoint).sqrMagnitude > 4f) Order(api, m, point, now);
            }
            // seats in the original order: the wheel first, then driver / passenger / turrets by slot; an arrived member waits for the ones before it
            if (needDriver)
            {
                var dm = g.DriverMember;
                if (dm != null && dm.State == Walking && dm.Arrived) Seat(g, dm, true);
            }
            if (crew.DriverSeatedAlive)
                for (int i = 0; i < g.Members.Count; i++)
                {
                    var m = g.Members[i];
                    if (m == g.DriverMember && needDriver || m.State == Aboard || m.State == Left) continue;
                    if (m.State != Walking || !m.Arrived) break;
                    Seat(g, m, false);
                }
            for (int i = 0; i < g.Members.Count; i++) if (g.Members[i].State == OnFoot || g.Members[i].State == Walking) return;
            if (Groups.Contains(g)) End(g, "everyone aboard", true);
        }

        private static bool Idle(Core.INpcApi api, GameObject npc)
        { try { return api != null && api.IsIdle(npc); } catch (System.Exception) { return false; } }
        private static int TravelState(Core.INpcApi api, GameObject npc)
        { try { return api != null ? api.TravelState(npc) : 3; } catch (System.Exception) { return 3; } }
        private static void Order(Core.INpcApi api, Member m, Vector3 point, float now)
        {
            m.OrderedAt = now; m.OrderedPoint = point;
            try { if (api != null) api.Travel(m.Body, point, ArriveRadius); } catch (System.Exception e) { PatrolsModule.Verbose("Return to car travel: " + e.Message); }
        }

        // beside the seat on its own side, the car's exit distance out (the bus: behind its rear door); kept in car space
        private static Vector3 BoardPoint(Group g, Member m)
        {
            var ct = g.Car.transform;
            if (!m.BoardKnown)
            {
                m.BoardKnown = true;
                float exit = VehicleRules.ExitDistance(g.Car.name) + 0.75f;
                Vector3 seat, world;
                var rear = TemplateExporter.PrefabName(g.Car.name).Equals("Rustliner", System.StringComparison.OrdinalIgnoreCase) ? Patrol.FindChild(ct, "hinge_door_parent") : null;
                if (m == g.DriverMember) seat = Patrol.DriverSeatPos(g.Car);
                else if (m.Role == RolePassenger)
                {
                    var sit = Patrol.FindChild(ct, "sitPos"); var hd = Patrol.FindChild(ct, "hinge_seat_driver"); var hp = Patrol.FindChild(ct, "hinge_seat_passenger");
                    seat = sit != null && hd != null && hp != null ? sit.position + (hp.position - hd.position) : ct.position + ct.right;
                }
                else
                {
                    var host = m.Slot >= PatrolMarker.VehicleTurretSlots ? TrailerSupport.Attached(g.Car) : g.Car;
                    seat = host != null && m.LocalPos != null && m.LocalPos.Length == 3 ? host.transform.TransformPoint(new Vector3(m.LocalPos[0], m.LocalPos[1], m.LocalPos[2])) : ct.position;
                }
                if (rear != null) world = rear.position - ct.forward * exit;
                else
                {
                    float x = ct.InverseTransformPoint(seat).x;
                    float side = Mathf.Abs(x) > 0.1f ? Mathf.Sign(x) : (m.Role == RolePassenger ? 1f : -1f);
                    world = seat + ct.right * side * exit;
                }
                world.y = seat.y - 1f;
                m.Board = ct.InverseTransformPoint(world);
            }
            return ct.TransformPoint(m.Board);
        }

        private static void Seat(Group g, Member m, bool asDriver)
        {
            var car = g.Car; var marker = g.Marker; var crew = g.Crew;
            float health = m.Health != null ? m.Health.Value : -1f;
            GameObject seated = null;
            if (asDriver)
            {
                crew.PrepareReboard();
                seated = Patrol.SeatLiveDriver(car, m.Identity, health, CrewPhase.Waiting, 0f, true, false);
                if (seated == null) { crew.MarkVacant(); Leave(g, m, "driver seat unavailable"); return; }
                marker.SetDriver(seated, m.Identity); marker.DriverDied = false; marker.DriverBailed = false; g.SeatedDriver = true;
                TrafficFactions.Actor(seated, marker.Faction);
            }
            else if (m.Role == RolePassenger || m.Role == RoleDriver)
            {
                // an original driver whose wheel was taken by another member sits in the passenger seat
                if (marker.Passenger != null) { Leave(g, m, "passenger seat taken"); return; }
                seated = Patrol.SeatPassenger(car, m.Identity, health, false);
                if (seated == null) { Leave(g, m, "passenger seat unavailable"); return; }
                marker.SetPassenger(seated, m.Identity);
                TrafficFactions.Actor(seated, marker.Faction);
            }
            else
            {
                GameObject platform = null;
                if (m.Slot >= PatrolMarker.VehicleTurretSlots) { platform = TrailerSupport.Attached(car); if (platform == null) { Leave(g, m, "trailer gone"); return; } }
                if (m.Slot < 0 || m.Slot >= marker.Riders.Length || marker.Riders[m.Slot] != null) { Leave(g, m, "turret spot taken"); return; }
                var oldAnchor = marker.RiderAnchors[m.Slot];
                if (oldAnchor != null) Object.Destroy(oldAnchor.gameObject);
                marker.RiderAnchors[m.Slot] = null;
                seated = Rider.Seat(car, marker, m.Slot, m.Who, m.LocalPos, platform, m.Standing, false);
                if (seated == null) { Leave(g, m, "turret could not be seated"); return; }
            }
            if (health > 0f) { Patrol.SetHealth(seated, health); seated.AddComponent<Patrol.LateHealth>().Value = health; }
            var body = m.Body;
            if(Core.Multiplayer.Hosting&&Core.Services.Npc!=null)Core.Services.Npc.TransferPlayerAwareness(body,seated,PatrolsModule.DismountMemorySeconds.Value);
            if (Core.Log.Verbose) PatrolsModule.Verbose("Return to car: " + body.name + " is back in " + car.name + (asDriver ? " at the wheel" : m.Role == RoleTurret ? " on turret " + (m.Slot + 1) : " in the passenger seat") + " as " + seated.name);
            Register.RemoveTree(body); Object.Destroy(body);
            m.Body = null; m.State = Aboard;
        }

        private static void Leave(Group g, Member m, string why)
        {
            bool alive = m.Body != null && (m.Health == null || m.Health.Value > 0f);
            m.State = Left;
            if (g.DriverMember == m) g.DriverMember = null;
            if (Core.Log.Verbose && alive) PatrolsModule.Verbose("Return to car: " + m.Body.name + " does not get back into " + (g.Car != null ? g.Car.name : "the car") + " (" + why + ")");
            if (!alive) return;
            var api = Core.Services.Npc;
            if (m.Arrived || m.OrderedAt > 0f) try { if (api != null) api.Travel(m.Body, null, 0f); } catch (System.Exception) { }
            if (m.Excluded) return;
            Strand(m.Body, m.Identity, g.Faction, g.Name);
        }

        private static void End(Group g, string why, bool finished)
        {
            Groups.Remove(g);
            var crew = g.Crew;
            if (crew != null && ReferenceEquals(crew.Regroup, g)) crew.Regroup = null;
            for (int i = 0; i < g.Members.Count; i++)
            {
                var m = g.Members[i];
                if (m.State == OnFoot || m.State == Walking) Leave(g, m, why);
            }
            bool driver = crew != null && crew.DriverSeatedAlive;
            if (crew != null && (crew.Regrouping || g.SeatedDriver)) crew.FinishReboard(g.SeatedDriver && driver);
            // the self-destruct was skipped while the car was held (Explode.Eligible): an empty car that keeps nobody gets its roll now
            if (crew != null && !driver && g.Car != null && g.Marker != null && !g.Marker.Exploded && !Explode.IsWreck(g.Car)) Explode.Schedule(crew, g.Car, g.Marker, "its crew did not come back");
            if (finished && driver && g.Car != null && !string.IsNullOrEmpty(g.TrafficId)) WorldTraffic.Reattach(g.Car, g.TrafficId, g.TrafficSlot);
            if (Core.Log.Verbose) PatrolsModule.Verbose("Return to car: " + (g.Car != null ? g.Car.name : "car") + " regroup ended (" + why + ")" + (finished && driver ? ", driving on" : ""));
        }

        // ------------------------------------------------------------ crew without a usable car (foot patrols, WorldTraffic)

        private static void Strand(GameObject body, string identity, string faction, string name)
        {
            if (body == null || !PatrolsModule.ConvoyEnabled.Value || !PatrolsModule.FootPatrols.Value || Core.Services.Npc == null) return;
            for (int i = 0; i < Stranded.Count; i++) if (ReferenceEquals(Stranded[i].Body, body)) return;
            Stranded.Add(new StrandedCrew { Body = body, Health = HealthVar(body), Identity = identity, Faction = TrafficFactions.Normalize(faction), Name = name });
        }

        internal static FsmFloat HealthVar(GameObject who)
        {
            if (who == null) return null;
            Fs.Clear(); who.GetComponents(Fs);
            FsmFloat result = null;
            for (int i = 0; i < Fs.Count; i++) if (Fs[i] != null && Fs[i].FsmName == "Health") { result = Fs[i].FsmVariables.GetFsmFloat("Health"); break; }
            Fs.Clear();
            return result;
        }
    }
}
