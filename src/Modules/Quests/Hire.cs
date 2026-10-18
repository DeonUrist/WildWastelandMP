using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HutongGames.PlayMaker;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Quests
{
    // Hired help. Talk to a Bodyguard, Pup, Bolter, Pistoleer, Gunnar or Lugger and spend reputation with their faction to have them come with you
    // for a day: they walk after you, ride in the passenger seat of your car when there is one, and go after whatever hurts you.
    // One at a time. Fighting is NPCAI's job (its public Api.Inform sends an NPC after a target) and the mod needs NPCAI and
    // Apocapatrol to be installed, so hiring is refused without them.
    internal static partial class Hire
    {
        public const string NpcAiGuid = "com.denis.apocalypter.npcai";
        public const string PatrolGuid = "com.denis.apocalypter.apocapatrol";

        static ConfigEntry<bool> enabled, rideCars;
        static ConfigEntry<string> prices;
        static ConfigEntry<float> durationHours, followDist, teleportDist, rideRange;
        static ConfigEntry<int> deathPenalty;
        static ConfigEntry<UnityEngine.InputSystem.Key> dismissKey;

        static QuestBook book;
        static Action<string> toast;
        static Action<string, int, string> changeRep;

        // ---- the follower -----------------------------------------------------------------------------
        internal static GameObject npc;
        static bool wasMountedAtLoss;
        static readonly List<Collider> lateOwn = new List<Collider>(), lateCar = new List<Collider>();
        static float restoreAt;
        static ConfigEntry<string> ignoreNames; static ConfigEntry<float> leash;
        static RigidbodyInterpolation wasInterp; static CollisionDetectionMode wasDetect; static bool wasGravity = true;
        static float outSince = -1f; const float ExitDelay = 1.5f;
        static bool endPending;
        static int rescues; static float firstRescue;
        static bool attached;                   // npc was set (it may have been destroyed since)
        static string displayName = "";
        static float searchUntil;               // real time: how long to look for the saved follower after a load
        static bool searching;
        static float lastDistance;              // last known distance to the player, to tell a death from an unload
        static PlayMakerFSM moveFsm, healthFsm;
        static float nextState, nextSteer, nextThreat, nextExclude;
        static float tooFarSince = -1f;
        static Vector3 heading;

        // fighting
        internal static GameObject target;
        static float engagedUntil;
        static float lastInform;

        // the player's health, to see when something hurts them
        static PlayMakerFSM playerHealth;
        static FsmFloat playerHealthVar;
        static float lastPlayerHealth = -1f;
        static float hurtUntil;
        static float nextHealthFind;
        static Transform watchedPlayer;

        // riding
        internal static bool mounted;
        static GameObject mountObj;
        static Transform origParent;
        static GameObject carObj;
        static float inCarSince = -1f;
        static readonly List<Behaviour> muted = new List<Behaviour>();
        static readonly List<Collider> ignoredCars = new List<Collider>();
        static readonly List<Collider> ownCols = new List<Collider>();
        static bool wasKinematic;
        static Component poseComp;
        static Component guardComp;                       // Apocapatrol's PassengerGuard on a seated gunner
        static readonly List<KeyValuePair<Renderer, bool>> rendSnap = new List<KeyValuePair<Renderer, bool>>();
        static readonly List<KeyValuePair<Collider, bool>> colSnap = new List<KeyValuePair<Collider, bool>>();
        static ConfigEntry<bool> shootFromCar;
        static ConfigEntry<float> shotDamage, shotInterval;
        static ConfigEntry<bool> realFire, shotAnim;
        static ConfigEntry<float> recoilDeg;
        static ConfigEntry<string> magSizes, reloadTimes, reloadSoundFrom;
        static bool recoilHooked;
        static ConfigEntry<string> rangedNames;
        static ConfigEntry<string> armPoseLike;
        static PlayMakerFSM inCarFsm;
        static float nextCarFind;


        static readonly string[] MuteNames = { "Movement", "Unstuck", "Rotate", "Detection", "Attack", "RangedAttackWait", "Damage Ranged" };

        public static bool Enabled { get { return enabled != null && enabled.Value; } }
        public static bool Active { get { return book != null && !string.IsNullOrEmpty(book.HireName); } }
        public static string Name { get { return displayName; } }

        public static void Init(HireSettings cfg, QuestBook b, Action<string> toastFn, Action<string, int, string> repFn)
        {
            book = b; toast = toastFn; changeRep = repFn;
            enabled = cfg.enabled;
            prices = cfg.prices;
            durationHours = cfg.durationHours;
            deathPenalty = cfg.deathPenalty;
            followDist = cfg.followDist;
            teleportDist = cfg.teleportDist;
            ignoreNames = cfg.ignoreNames;
            leash = cfg.leash;
            rideCars = cfg.rideCars;
            shootFromCar = cfg.shootFromCar;
            realFire = cfg.realFire;
            shotAnim = cfg.shotAnim;
            recoilDeg = cfg.recoilDeg;
            if (!recoilHooked) { recoilHooked = true; Application.onBeforeRender += Recoil; }
            magSizes = cfg.magSizes;
            reloadTimes = cfg.reloadTimes;
            reloadSoundFrom = cfg.reloadSoundFrom;
            shotDamage = cfg.shotDamage;
            shotInterval = cfg.shotInterval;
            rangedNames = cfg.rangedNames;
            armPoseLike = cfg.armPoseLike;
            rideRange = cfg.rideRange;
            dismissKey = cfg.dismissKey;
        }

        // ---- prices ---------------------------------------------------------------------------------------
        // the cost in reputation of hiring an NPC with this name, or -1 when it is not for hire
        public static int CostFor(string npcName)
        {
            if (!Enabled || string.IsNullOrEmpty(npcName)) return -1;
            string n = Util.Norm(npcName);
            foreach (string part in (prices.Value ?? "").Split(','))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = Util.Norm(part.Substring(0, eq));
                int cost;
                if (key.Length == 0 || !int.TryParse(part.Substring(eq + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out cost)) continue;
                if (cost > 0 && Util.Norm(Game.BaseName(npcName)).StartsWith(key, StringComparison.Ordinal)) return cost;
            }
            return -1;
        }

        // "NPCAI and Apocapatrol" etc.; empty when both are there
        public static string MissingMods() { return Services.Npc == null ? "NPC AI" : Services.FollowerVehicles == null ? "Patrols" : ""; }

        public static bool IsFollower(Transform t)
        {
            return Active && t != null && npc != null && (t.gameObject == npc || t.IsChildOf(npc.transform));
        }

        // ---- start / stop ---------------------------------------------------------------------------------------
        public static void Start(GameObject go, string factionKey, string shownName)
        {
            if (go == null) return;
            MultiplayerParty.Follow(PartyQuests.RemotePeer);
            double gh = Game.GameHours();
            book.HireName = go.name;
            book.HireUntil = gh >= 0.0 ? gh + Math.Max(0.1f, durationHours.Value) : -1.0;
            book.HireFaction = factionKey ?? "";
            book.HireKind = shownName ?? "";
            Attach(go);
            toast("Hired " + displayName + " for " + (durationHours.Value >= 24f ? Math.Round(durationHours.Value / 24f, 1).ToString("0.#", CultureInfo.InvariantCulture) + " day(s)" : durationHours.Value.ToString("0.#", CultureInfo.InvariantCulture) + " hours") + ". " + dismissHint());
            Plugin.Log.LogInfo("Hired " + go.name + " (" + book.HireKind + ") until game hour " + book.HireUntil.ToString("0.0", CultureInfo.InvariantCulture));
        }

        static string dismissHint()
        {
            return dismissKey.Value != UnityEngine.InputSystem.Key.None ? "Press " + dismissKey.Value + " to send them away." : "Talk to them to send them away.";
        }

        static void Attach(GameObject go)
        {
            npc = go;
            attached = true;
            magLeft = -1; footSetup = false; footShooting = false;
            fsmSnap.Clear();
            foreach (PlayMakerFSM f in QuestWorld.Fsms(go)) if (f != null) fsmSnap.Add(new KeyValuePair<PlayMakerFSM, bool>(f, f.enabled));
            displayName = string.IsNullOrEmpty(book.HireKind) ? Game.BaseName(go.name) : book.HireKind;
            moveFsm = FindFsm(go, "Movement");
            healthFsm = FindFsm(go, "Health");
            target = null;
            engagedUntil = 0f;
            mounted = false;
            searching = false;
            tooFarSince = -1f;
            heading = Vector3.zero;
            lastPlayerHealth = -1f;
            lastDistance = 0f;
            wasMountedAtLoss = false;
            Game.InvalidateLiveNames();
        }

        // Ends the hire. The NPC goes back to being a normal NPC where it stands.
        public static void Dismiss(string message)
        {
            if (!Active) return;
            MultiplayerParty.Follow(0);
            if (mounted) Dismount(true);
            ReleaseNpc();
            string n = displayName.Length > 0 ? displayName : (book.HireKind ?? "your help");
            Clear();
            if (!string.IsNullOrEmpty(message)) toast(message.Replace("{name}", n));
        }

        static readonly List<KeyValuePair<PlayMakerFSM, bool>> fsmSnap = new List<KeyValuePair<PlayMakerFSM, bool>>();
        static float settleUntil;

        // After the car: Apocapatrol's crew code switches off his walking AI (Movement, Rotate, Unstuck...) and may do it once more
        // as he gets out. Without Movement he cannot follow, and Unstuck makes him hop on the spot. For a couple of seconds after
        // getting out, everything but the combat FSMs (which Detection runs) is put back the way it was when he was hired.
        static void WakeBrain(bool log)
        {
            if (npc == null) return;
            try
            {
                int fixedCount = 0;
                foreach (var kv in fsmSnap)
                {
                    PlayMakerFSM f = kv.Key;
                    if (f == null) continue;
                    string n = f.FsmName;
                    if (n == "Attack" || n == "RangedAttackWait" || n == "Damage Ranged" || n == "Damage") continue;
                    bool want = kv.Value || n == "Detection" || n == "Movement";
                    if (f.enabled != want) { f.enabled = want; fixedCount++; }
                }
                if (fixedCount > 0 && (log || !wakeLogged)) { wakeLogged = true; Plugin.Log.LogInfo("Hire: " + displayName + "'s walking AI switched back on (" + fixedCount + " FSMs)."); }
            }
            catch (Exception e) { Plugin.LogOnce("HireWakeBrain", e); }
        }
        static bool wakeLogged;

        // After a save is loaded the game rebuilds the follower from its own save: he can come back marked hostile to the player or with the
        // player as the thing he has detected, and then he shoots you. Hired help is always friendly, so the hostility switch is put back
        // and a player target is cleared (on finding him again, and every couple of seconds after that, quickly for the first 20 s).
        static float nextCalm, calmFast;
        static void Calm(Transform pl, bool log)
        {
            if (npc == null) return;
            try
            {
                if (log) calmFast = Time.time + 20f;
                bool did = false;
                if (Game.IsPlayerEnemy(npc) && Game.SetPlayerEnemy(npc, false)) did = true;
                foreach (PlayMakerFSM f in QuestWorld.Fsms(npc))
                {
                    if (f == null || (f.FsmName != "Detection" && f.FsmName != "Attack")) continue;
                    FsmGameObject v = f.FsmVariables.GetFsmGameObject("detectedObj");
                    if (v == null || v.Value == null || pl == null) continue;
                    if (v.Value == pl.gameObject || v.Value.transform.IsChildOf(pl) || pl.IsChildOf(v.Value.transform))
                    {
                        v.Value = null;
                        if (f.FsmName == "Detection") { try { f.Fsm.SetState("notDetected"); } catch (Exception) { } }
                        did = true;
                    }
                }
                if (did) Plugin.Log.LogInfo("Hire: " + displayName + " was hostile to you (after a load); made friendly again.");
            }
            catch (Exception e) { Plugin.LogOnce("HireCalm", e); }
        }

        static void ReleaseNpc()
        {
            if (npc == null) return;
            try
            {
                if (moveFsm != null && moveFsm.ActiveStateName == "Run") moveFsm.Fsm.SetState("Idle");
                SetExcluded(false);
            }
            catch (Exception) { }
            RestoreBrain();
        }

        // Put his own brain back the way it was when he was hired: the car, Apocapatrol's gunner and the target the mod fed him
        // can leave Detection or other FSMs switched off, and then he just stands there after being let go.
        static void RestoreBrain()
        {
            if (npc == null) return;
            try
            {
                foreach (Component c in npc.GetComponents<Component>())
                {
                    if (c == null) continue;
                    string tn = c.GetType().FullName;
                    if (tn == "WildWasteland.Modules.Patrols.PassengerGuard" || tn == "WildWasteland.Modules.Patrols.Pose") UnityEngine.Object.Destroy(c);
                }
                foreach (var kv in fsmSnap) if (kv.Key != null) kv.Key.enabled = kv.Value;
                foreach (PlayMakerFSM f in QuestWorld.Fsms(npc))
                {
                    if (f == null) continue;
                    if (f.FsmName == "Detection" || f.FsmName == "Attack")
                    {
                        FsmGameObject v = f.FsmVariables.GetFsmGameObject("detectedObj");
                        if (v != null) v.Value = null;
                    }
                    if (f.FsmName == "Detection")
                    {
                        f.enabled = true;
                        try { f.Fsm.SetState("notDetected"); } catch (Exception) { }
                    }
                    if (f.FsmName == "Movement" || f.FsmName == "Rotate" || f.FsmName == "Unstuck") f.enabled = true;
                }
                Plugin.Log.LogInfo("Hire: " + displayName + "'s own AI is back on.");
            }
            catch (Exception e) { Plugin.LogOnce("HireRestoreBrain", e); }
            fsmSnap.Clear();
        }

        static void Clear()
        {
            book.HireName = ""; book.HireUntil = 0.0; book.HireFaction = ""; book.HireKind = "";
            npc = null; attached = false; target = null; moveFsm = null; healthFsm = null; searching = false; mounted = false; displayName = "";
            carObj = null; inCarSince = -1f; endPending = false; guardComp = null;
        }

        // new world / slot being loaded: forget the objects (the book itself is reloaded by the caller)
        public static void Reset()
        {
            if (mounted) { try { Dismount(false); } catch (Exception) { } }
            ReleaseNpc(); QuestFollower.Release(); RestoreCollisions();
            npc = null; attached = false; target = null; moveFsm = null; healthFsm = null; searching = false; mounted = false; displayName = "";
            carObj = null; inCarSince = -1f; endPending = false; playerHealth = null; playerHealthVar = null; lastPlayerHealth = -1f; inCarFsm = null;
            Ranged.Clear(); _nearestName = null;
            muted.Clear(); ignoredCars.Clear(); ownCols.Clear(); lateOwn.Clear(); lateCar.Clear();
        }

        static PlayMakerFSM FindFsm(GameObject go, string name)
        {
            if (go == null) return null;
            PlayMakerFSM[] fsms = QuestWorld.Fsms(go);
            for (int i = 0; i < fsms.Length; i++) if (fsms[i] != null && fsms[i].FsmName == name) return fsms[i];
            return null;
        }

        // the loaded NPC with this object name that is closest to the player (names can repeat for NPCs the game did not number)
        static Transform NearestUncached(string name, Vector3 from)
        {
            Transform best = null;
            float bd = float.MaxValue;
            var all = QuestWorld.People;
            if (all == null) return null;
            for (int i = 0; i < all.Count; i++)
            {
                PlayMakerFSM f = all[i];
                if (f == null || f.FsmName != "Health") continue;
                GameObject go = f.gameObject;
                if (go == null || go.name != name) continue;
                float d = Game.HDist(go.transform.position, from);
                if (d < bd) { bd = d; best = go.transform; }
            }
            return best;
        }

        static Transform PartyPlayer() { var player = Multiplayer.Hosting ? MultiplayerParty.FollowPlayer : null; return player != null ? player.transform : Game.Player(); }

        // ---- every frame ----------------------------------------------------------------------------------------
        public static void Update()
        {
            
            if (!Enabled || book == null) return;
            RestoreCollisions();                // also after the hire has ended, so a dropped-off follower stops ignoring the car
            if (!Active) return;

            Transform pl = PartyPlayer();
            if (pl == null) return;

            // after a load the saved follower has to be found again by name
            if (!attached)
            {
                if (!searching) { searching = true; searchUntil = Time.unscaledTime + 60f; }
                Transform t = NearestByName(book.HireName, pl.position);
                if (t != null) { Attach(t.gameObject); Plugin.Log.LogInfo("Hired help " + book.HireName + " found again."); Calm(pl, true); nextCalm = Time.time + 0.5f; }
                else if (Time.unscaledTime > searchUntil)
                {
                    string n = string.IsNullOrEmpty(book.HireKind) ? "Your hired help" : book.HireKind;
                    Clear();
                    toast(n + " is gone. The hire is over.");
                }
                return;
            }

            // dead, or gone
            if (npc == null) { Gone(); return; }
            wasMountedAtLoss = mounted;
            if (healthFsm != null && healthFsm.ActiveStateName != "health" && healthFsm.Fsm != null && healthFsm.Fsm.Initialized) { Died(); return; }

            double gh = Game.GameHours();
            if (book.HireUntil > 0.0 && gh >= 0.0 && gh >= book.HireUntil)
            {
                // riding: they stay in the car until you get out
                if (mounted) { if (!endPending) { endPending = true; toast(displayName + "'s day is up. They'll get out when you do."); } }
                else { Dismiss("{name}'s day is up. They head back."); return; }
            }

            float dist = Game.HDist(npc.transform.position, pl.position);
            lastDistance = dist;
            float now = Time.time;

            WatchPlayerHealth(pl);
            bool inCar;
            GameObject car = PlayerCar(pl, out inCar);

            // riding
            if (!rideCars.Value && mounted) Dismount(true);
            if (rideCars.Value)
            {
                if (inCar && car != null)
                {
                    if (!mounted)
                    {
                        if (inCarSince < 0f) inCarSince = now;
                        if (now - inCarSince > 1.2f && dist <= rideRange.Value) { if (!TryMount(car)) inCarSince = now + 30f; }     // no seat: do not try again for a while
                    }
                    else if (carObj != car) Dismount(true);
                    else outSince = -1f;
                }
                else
                {
                    inCarSince = -1f;
                    if (mounted)
                    {
                        // wait a moment after you step out, so the ground and the car settle before the follower gets out
                        if (outSince < 0f) outSince = now;
                        if (now - outSince >= ExitDelay) Dismount(true);
                    }
                }
            }
            if (mounted)
            {
                if (now >= nextColCheck) { nextColCheck = now + 0.2f; IgnoreNewColliders(); Rigidbody mrb = npc.GetComponent<Rigidbody>(); if (mrb != null && !mrb.isKinematic) { mrb.velocity = Vector3.zero; mrb.isKinematic = true; } }
                if (guardComp != null) { Threats(pl, dist); ExcludeFromIdle(); FeedTarget(); TurnToTarget(); HoldFire(); PlayDueSounds(); WatchShots(); FireShots(); LogGuard(); }       // the gunner needs targets from NPCAI
                return;                         // the seat is kept in LateUpdate
            }
            if (endPending) { Dismiss("{name} has gone their own way."); return; }
            if (now < settleUntil) WakeBrain(false);

            if (now >= nextCalm) { nextCalm = now + (now < calmFast ? 0.5f : 2f); Calm(pl, false); }
            RescueIfLost(pl);
            Threats(pl, dist);
            FootWatch(now);
            ExcludeFromIdle();
            Follow(pl, dist, now);
            CatchUp(pl, dist, now);
        }

        public static void LateUpdate()
        {
            if (!mounted || npc == null || mountObj == null) return;
            if (guardComp != null) FeedTarget();
            // keep the passenger in the seat: other mods and the NPC's own AI may try to move it
            Transform t = npc.transform;
            if (t.parent != mountObj.transform) t.SetParent(mountObj.transform, false);
            if (poseComp == null) { t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; }
        }

        // ---- the player's health: something hurt them --------------------------------------------------
        static void WatchPlayerHealth(Transform pl)
        {
            if (watchedPlayer != pl) { watchedPlayer = pl; playerHealth = null; playerHealthVar = null; lastPlayerHealth = -1f; nextHealthFind = 0; }
            if (Multiplayer.IsRemotePlayer(pl.gameObject))
            {
                float health = MultiplayerParty.PlayerHealth(pl.gameObject);
                if (!float.IsNaN(health)) { if (lastPlayerHealth >= 0f && health < lastPlayerHealth - .001f) hurtUntil = Time.time + 8f; lastPlayerHealth = health; }
                return;
            }
            if (playerHealthVar == null)
            {
                if (Time.unscaledTime < nextHealthFind) return;
                nextHealthFind = Time.unscaledTime + 2f;
                PlayMakerFSM[] all = pl.GetComponentsInChildren<PlayMakerFSM>(true);
                if (all.Length == 0 || FindFsm(pl.gameObject, "Health") == null) all = pl.GetComponentsInParent<PlayMakerFSM>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null || all[i].FsmName != "Health") continue;
                    FsmFloat v = Game.FindFloat(all[i], "Health");
                    if (v != null) { playerHealth = all[i]; playerHealthVar = v; break; }
                }
                if (playerHealthVar == null) return;
            }
            float h = playerHealthVar.Value;
            if (lastPlayerHealth >= 0f && h < lastPlayerHealth - 0.001f) hurtUntil = Time.time + 8f;
            lastPlayerHealth = h;
        }

        // ---- fighting ------------------------------------------------------------------------------------------
        static bool Alive(GameObject go)
        {
            if (go == null) return false;
            PlayMakerFSM h = FindFsm(go, "Health");
            return h == null || h.ActiveStateName == "health";
        }

        // an enemy of the player: the game's own switch says so, or it is a creature with no such switch
        static bool IsCritter(GameObject go)
        {
            string n = Game.BaseName(go.name);
            foreach (string part in (ignoreNames.Value ?? "").Split(','))
            {
                string q = part.Trim();
                if (q.Length > 0 && n.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        static float footSince = -1f, footShotAt; static GameObject ignoreTarget; static float ignoreUntil;

        // On foot, Follow waits while he has a target (NPCAI steers the fight). A target he cannot get at (in a car, out of sight)
        // would keep him standing there for good, so after 8 s without attacking he gives up on it for a while and follows again.
        static string lastFootAtk; static bool footHeld;
        static string lastSnd, lastDr; static float selfShotAt = -10f, lastRealShot = -10f;

        // One real shot of his own: the gunshot (Sound2 starting to play) or a hit/shot state of his Damage Ranged FSM.
        // Both usually come with the same shot, so signals closer than 40 ms count once. The mod's own shots are not counted here.
        static bool RealShotFired()
        {
            bool fired = false;
            try
            {
                PlayMakerFSM snd = FindFsm(npc, "Sound2"), dr = FindFsm(npc, "Damage Ranged");
                string ss = snd != null && snd.enabled ? snd.ActiveStateName : null;
                string ds = dr != null && dr.enabled ? dr.ActiveStateName : null;
                if (ss == "playAudio" && lastSnd != "playAudio" && Time.time - selfShotAt > 0.15f) fired = true;
                if ((ds == "attack" || ds == "shoot") && lastDr != ds) fired = true;
                lastSnd = ss; lastDr = ds;
                if (fired && Time.time - lastRealShot < 0.04f) fired = false;
                if (fired) lastRealShot = Time.time;
                if (fired && magSize > 0 && magLeft == 1 && dr != null) dr.enabled = false;     // last round: no second shot sneaks out before the reload starts
            }
            catch (Exception e) { Plugin.LogOnce("HireRealShot", e); }
            return fired;
        }

        // His own shooting on foot counts against the same magazine; while he reloads his Attack FSM is held off,
        // and given back (if Detection still sees an enemy) when the reload is done.
        static void FootMagazine(float now)
        {
            PlayDueSounds();
            try
            {
                PlayMakerFSM atk = null, det = null, dr = null, raw = null;
                foreach (PlayMakerFSM f in QuestWorld.Fsms(npc))
                {
                    if (f == null) continue;
                    if (f.FsmName == "Attack") atk = f; else if (f.FsmName == "Detection") det = f; else if (f.FsmName == "Damage Ranged") dr = f; else if (f.FsmName == "RangedAttackWait") raw = f;
                }
                string st = atk != null && atk.enabled ? atk.ActiveStateName : null;
                if (RealShotFired())
                {
                    if (!footSetup) { footSetup = true; GunSetup(); footShootAnim = ShootAnimName(); }
                    CountShot();
                }
                lastFootAtk = st;
                if (Reloading)
                {
                    if (atk != null && atk.enabled) { atk.enabled = false; footHeld = true; }
                    if (dr != null && dr.enabled) { dr.enabled = false; footHeld = true; }
                }
                else if (footHeld)
                {
                    footHeld = false;
                    if (det != null && det.enabled && det.ActiveStateName == "inLOS")
                    {
                        if (atk != null) atk.enabled = true;
                        if (raw != null) raw.enabled = true;
                    }
                }
            }
            catch (Exception e) { Plugin.LogOnce("HireFootMag", e); }
        }

        static void FootWatch(float now)
        {
            FootMagazine(now);
            if (target == null) { footSince = -1f; EndFootShot(); return; }
            if (!Alive(target)) { target = null; footSince = -1f; EndFootShot(); return; }
            if (footSince < 0f) { footSince = now; footShotAt = now; }
            foreach (PlayMakerFSM f in QuestWorld.Fsms(npc))
            {
                if (f == null || !f.enabled) continue;
                string st = f.ActiveStateName;
                if ((f.FsmName == "Attack" && (st == "attack_ranged" || st == "attack_melee")) || (f.FsmName == "Damage Ranged" && st == "attack")) footShotAt = now;
            }
            // a shooter whose own AI does not get the shot off (the target is in a car, his sensors do not see it...) shoots the way he does from the car
            bool ranged = shootFromCar.Value && IsRangedNpc(npc);
            float d = Vector3.Distance(target.transform.position, npc.transform.position);
            if (ranged && d <= PatrolShootDistance())
            {
                if (now - footShotAt < 3f && !footShooting) return;          // his own fighting works
                FootShoot(now);
                return;
            }
            EndFootShot();
            if (now - footShotAt < 8f) return;
            Plugin.Log.LogInfo("Hire: " + displayName + " cannot get at " + target.name + "; follows you again.");
            ignoreTarget = target; ignoreUntil = now + 20f;
            target = null; engagedUntil = 0f; footSince = -1f;
        }

        static bool footShooting, footSetup; static float footNextShot, footIdleAt; static string footShootAnim;

        static void FootShoot(float now)
        {
            if (!footShooting) { footShooting = true; Plugin.Log.LogInfo("Hire: " + displayName + " shoots at " + target.name + " on foot."); }
            if (!footSetup) { footSetup = true; GunSetup(); footShootAnim = ShootAnimName(); }
            // stand and face the target
            if (moveFsm != null && moveFsm.ActiveStateName == "Run") { try { moveFsm.Fsm.SetState("Idle"); } catch (Exception) { } }
            Vector3 dir = target.transform.position - npc.transform.position; dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) npc.transform.rotation = Quaternion.Slerp(npc.transform.rotation, Quaternion.LookRotation(dir), 8f * Time.deltaTime);
            PlayDueSounds();
            if (footIdleAt > 0f && now > footIdleAt) { footIdleAt = 0f; if (moveFsm != null) { try { moveFsm.Fsm.SetState("Idle"); } catch (Exception) { } } }
            if (Reloading || now < footNextShot || shotDamage.Value <= 0f) return;
            if (Vector3.Angle(npc.transform.forward, dir) > 25f) return;                 // still turning
            if (!ClearShot()) return;
            footNextShot = now + Mathf.Max(0.2f, shotInterval.Value) * UnityEngine.Random.Range(0.8f, 1.3f);
            try
            {
                PlayMakerFSM hf = FindFsm(target, "Health");
                FsmFloat hp = hf != null ? hf.FsmVariables.GetFsmFloat("Health") : null;
                if (hp == null) return;
                hp.Value -= ownDamage > 0f ? ownDamage : shotDamage.Value;
                shotsFired++;
                if (shotsFired <= 12) Plugin.Log.LogInfo("Hire: " + displayName + " shoots " + target.name + " on foot (health now " + hp.Value.ToString("0") + ").");
                if (npcAnim == null) npcAnim = npc.GetComponent<Animator>() ?? npc.GetComponentInChildren<Animator>(true);
                if (npcAnim != null && !string.IsNullOrEmpty(footShootAnim)) { QuestFollower.Play(npcAnim, footShootAnim, 0, 0f); footIdleAt = now + 0.9f; }
                PlayShotAnim();
                CountShot();
                PlayMakerFSM snd = FindFsm(npc, "Sound2");
                if (snd != null) { selfShotAt = Time.time; try { snd.Fsm.SetState("playAudio"); } catch (Exception) { } }
            }
            catch (Exception e) { Plugin.LogOnce("HireFootShoot", e); }
        }

        static void EndFootShot()
        {
            if (!footShooting) return;
            footShooting = false;
            if (footIdleAt > 0f && moveFsm != null) { try { moveFsm.Fsm.SetState("Idle"); } catch (Exception) { } }
            footIdleAt = 0f;
            if (flashObjs != null) foreach (GameObject g in flashObjs) if (g != null) g.SetActive(false);
        }

        // nothing solid between his chest and the target (the target's own car does not count)
        static bool ClearShot() { return ClearShot(target); }

        static bool ClearShot(GameObject target)
        {
            if (target != null && target == coverTarget && Time.time < coverUntil) return true;      // it hurt you from behind cover: shoot anyway
            try
            {
                Vector3 from = npc.transform.position + Vector3.up * 1.5f, to = target.transform.position + Vector3.up * 1.0f;
                Vector3 d = to - from; float len = d.magnitude;
                if (len < 0.5f) return true;
                Transform troot = target.transform.root;
                foreach (RaycastHit h in QuestWorld.RaycastAll(from, d / len, len))
                {
                    if (h.collider == null || h.collider.isTrigger) continue;
                    Transform t = h.collider.transform;
                    if (t.IsChildOf(npc.transform) || t.IsChildOf(target.transform)) continue;
                    if (mounted && carObj != null && t.IsChildOf(carObj.transform)) continue;          // his own car
                    // what the target rides in (or sits in) only counts when the hit is at the target itself, not a hull or wall in front of him
                    if (t.IsChildOf(troot) && Vector3.Distance(h.point, to) < 0.7f) continue;
                    Transform pl = PartyPlayer();
                    if (pl != null && t.IsChildOf(pl.root)) continue;
                    return false;
                }
            }
            catch (Exception) { }
            return true;
        }

        // the Animator state his Movement FSM plays to shoot on foot ("attack 2", "attack 4"...)
        static string ShootAnimName()
        {
            try
            {
                PlayMakerFSM mv = moveFsm ?? FindFsm(npc, "Movement");
                if (mv == null) return null;
                foreach (FsmState st in mv.FsmStates)
                {
                    if (st.Name != "AttackRanged") continue;
                    foreach (FsmStateAction a in st.Actions)
                    {
                        if (a == null) continue;
                        FieldInfo fi = a.GetType().GetField("stateName");
                        FsmString v = fi != null ? fi.GetValue(a) as FsmString : null;
                        if (v != null && !string.IsNullOrEmpty(v.Value)) return v.Value;
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        static bool Hostile(GameObject go)
        {
            PlayMakerFSM[] fsms = QuestWorld.Fsms(go);
            for (int i = 0; i < fsms.Length; i++)
                if (fsms[i] != null && fsms[i].FsmName == "PlayerIsEnemy") return fsms[i].ActiveStateName == "PlayerEnemy";
            return true;
        }

        static bool FindApi() { return Services.Npc != null; }



        static void Inform(GameObject enemy)
        {
            if (npc == null || enemy == null || Services.Npc == null) return;
            Services.Npc.Inform(npc, enemy); target = enemy; lastInform = Time.time; engagedUntil = Time.time + 25f;
        }

        // Sends the follower after whatever hurt you (the closest living enemy near you) and keeps it on a target while one is close.
        static float hiddenSince = -1f, coverUntil; static GameObject coverTarget;
        static void Threats(Transform pl, float dist)
        {
            float now = Time.time;
            // still busy with a target?
            if (target != null && !Alive(target)) target = null;
            if (target != null && (now > engagedUntil || Game.HDist(target.transform.position, npc.transform.position) > 80f)) target = null;
            if (target != null && mounted && guardComp != null && Game.HDist(target.transform.position, pl.position) > PatrolShootDistance() * 1.25f) target = null;
            if (target != null && leash.Value > 0f && Game.HDist(target.transform.position, pl.position) > leash.Value) { target = null; if (!mounted && moveFsm != null && moveFsm.ActiveStateName != "Run") { try { moveFsm.Fsm.SetState("Run"); } catch (Exception) { } } }
            if (target != null && mounted && shootFromCar.Value && IsRangedNpc(npc))
            {
                if (ClearShot(target)) hiddenSince = -1f;
                else if (hiddenSince < 0f) hiddenSince = now;
                else if (now - hiddenSince > 1.2f) { target = null; hiddenSince = -1f; }          // behind a wall for a moment: stop aiming at it
            }
            if (target != null && now - lastInform > 4f) Inform(target);          // keep it on the job

            if (now < nextThreat) return;
            nextThreat = now + 0.7f;

            bool hurt = now < hurtUntil;
            if (target != null && !hurt) return;
            float radius = hurt ? 55f : 10f;
            if (mounted && guardComp != null) radius = PatrolShootDistance();          // in the car: only what Apocapatrol's gunner could shoot at
            List<EnemyInfo> foes = Game.FindEnemies(pl.position, radius);
            bool shooter = shootFromCar.Value && IsRangedNpc(npc);
            GameObject best = null;
            float bd = float.MaxValue;
            for (int i = 0; i < foes.Count; i++)
            {
                GameObject g = foes[i].Go;
                if (g == null || g == npc || g.transform.IsChildOf(npc.transform)) continue;
                if (g == ignoreTarget && now < ignoreUntil && !mounted) continue;
                if (!Hostile(g)) continue;
                if (!hurt && IsCritter(g)) continue;
                if (shooter && !hurt && !ClearShot(g)) continue;                        // a shooter does not pick (or aim at) someone behind a wall
                float d = Game.HDist(g.transform.position, pl.position);
                if (d < bd) { bd = d; best = g; }
            }
            if (best != null && hurt && shooter && !ClearShot(best)) { coverTarget = best; coverUntil = now + 8f; }      // hurt from behind cover: he still answers it
            else if (best != null && best == coverTarget && hurt) coverUntil = now + 8f;
            if (best != null && best != target) Inform(best);
        }

        // NPCAI's idle behaviour sends NPCs home; for the follower it is switched off (best effort, through NPCAI's own table)
        static void ExcludeFromIdle()
        {
            if (Time.unscaledTime < nextExclude) return;
            nextExclude = Time.unscaledTime + 1f;
            SetExcluded(true);
        }

        static void SetExcluded(bool value) { if (Services.Npc != null) Services.Npc.SetFollowerDriven(npc, value); }

        // ---- walking after you -----------------------------------------------------------------------------------
        static void Follow(Transform pl, float dist, float now)
        {
            if (moveFsm == null) { moveFsm = FindFsm(npc, "Movement"); if (moveFsm == null) return; }
            string ms = moveFsm.ActiveStateName;
            if (target != null || ms == "Attack") return;                 // fighting: NPCAI is steering

            Vector3 to = pl.position - npc.transform.position;
            to.y = 0f;
            float stop = Mathf.Max(2.5f, followDist.Value);
            if (dist <= stop)
            {
                if (now >= nextState && ms == "Run") { nextState = now + 0.25f; moveFsm.Fsm.SetState("Idle"); }
                return;
            }
            if (dist < stop + 1.5f && ms != "Run") return;                 // a little slack so it does not twitch at the edge
            if (to.sqrMagnitude < 0.01f) return;
            if (now >= nextSteer) { nextSteer = now + 0.3f; heading = Takeover.Steer(npc.transform, to.normalized, 3.5f); }
            bool boxedIn = heading.sqrMagnitude < 0.01f;
            Vector3 face = boxedIn ? to.normalized : heading;
            npc.transform.rotation = Quaternion.Slerp(npc.transform.rotation, Quaternion.LookRotation(face), 8f * Time.deltaTime);
            if (now >= nextState)
            {
                nextState = now + 0.25f;
                if (boxedIn) { if (ms == "Run") moveFsm.Fsm.SetState("Idle"); }
                else if (ms != "Run") moveFsm.Fsm.SetState("Run");
            }
        }

        // far behind for a few seconds: pop back next to the player
        static void CatchUp(Transform pl, float dist, float now)
        {
            float limit = teleportDist.Value;
            if (limit <= 0f) return;
            if (dist <= limit) { tooFarSince = -1f; return; }
            if (tooFarSince < 0f) { tooFarSince = now; return; }
            if (now - tooFarSince < 4f) return;
            tooFarSince = -1f;
            Vector3 back = -pl.forward; back.y = 0f;
            if (back.sqrMagnitude < 0.01f) back = Vector3.back;
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = pl.position + Quaternion.Euler(0f, i * 60f, 0f) * back.normalized * 4f;
                Vector3 g;
                if (Game.GroundAt(p.x, p.z, out g) && Takeover.RoomAt(g))
                {
                    npc.transform.position = g + Vector3.up * 0.2f;
                    LiftAbove(g.y);
                    Rigidbody rb = npc.GetComponent<Rigidbody>();
                    if (rb != null && !rb.isKinematic) rb.velocity = Vector3.zero;
                    Plugin.Log.LogInfo("Hire: " + displayName + " caught up.");
                    return;
                }
            }
        }


        // a free spot on the ground a few metres from the player (not on the car, not inside anything)
        static Vector3 FindExit(Transform pl, GameObject car)
        {
            int myLayer = npc != null ? npc.layer : 0;
            float[] radii = { 2.5f, 3.5f, 5f, 7f };
            for (int r = 0; r < radii.Length; r++)
                for (int a = 0; a < 8; a++)
                {
                    Vector3 dir = Quaternion.Euler(0f, a * 45f, 0f) * pl.right;
                    Vector3 p = pl.position + dir * radii[r];
                    // from well above, so a rise in the ground cannot hide the surface; the first surface the NPC can actually stand on
                    RaycastHit[] hits = QuestWorld.RaycastAll(new Vector3(p.x, pl.position.y + 40f, p.z), Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
                    Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
                    for (int i = 0; i < hits.Length; i++)
                    {
                        RaycastHit h = hits[i];
                        Transform ht = h.collider.transform;
                        if (car != null && ht.IsChildOf(car.transform)) continue;
                        if (npc != null && ht.IsChildOf(npc.transform)) continue;
                        if (ht.root == pl.root) continue;
                        if (Physics.GetIgnoreLayerCollision(myLayer, h.collider.gameObject.layer)) continue;     // the NPC would fall straight through this
                        if (h.collider.GetType().Name == "TerrainCollider" || h.normal.y >= 0.6f)
                        {
                            if (!Takeover.RoomAt(h.point)) break;                                              // something stands there
                            if (h.point.y > pl.position.y + 4f || h.point.y < pl.position.y - 6f) break;       // a roof or a pit, not the ground you stand on
                            Plugin.Log.LogInfo("Hire: exit spot on " + h.collider.name + " (layer " + h.collider.gameObject.layer + ", " + h.collider.GetType().Name + "), npc layer " + myLayer + ", y " + h.point.y.ToString("0.0") + ", player y " + pl.position.y.ToString("0.0"));
                            return h.point + Vector3.up * 0.15f;
                        }
                        break;
                    }
                }
            Plugin.Log.LogInfo("Hire: no free exit spot found; using your position.");
            return pl.position + Vector3.up * 0.3f;
        }

        // the NPC's origin is not at its feet: lift it until its lowest solid collider is just above the ground
        // (a body that starts inside a terrain falls straight through it)
        static void LiftAbove(float groundY)
        {
            if (npc == null) return;
            Physics.SyncTransforms();
            float minY = float.MaxValue;
            foreach (Collider c in npc.GetComponentsInChildren<Collider>(false))
                if (c != null && c.enabled && !c.isTrigger) minY = Math.Min(minY, c.bounds.min.y);
            if (minY == float.MaxValue) { npc.transform.position += Vector3.up * 0.6f; return; }
            float want = groundY + 0.12f;
            if (minY < want) npc.transform.position += Vector3.up * (want - minY);
            Physics.SyncTransforms();
        }

        // puts the car back in the NPC's way once it has had time to walk off
        static void RestoreCollisions()
        {
            if (lateOwn.Count == 0 || Time.time < restoreAt) return;
            for (int i = 0; i < lateOwn.Count; i++)
                for (int k = 0; k < lateCar.Count; k++)
                    if (lateOwn[i] != null && lateCar[k] != null) Physics.IgnoreCollision(lateOwn[i], lateCar[k], false);
            lateOwn.Clear(); lateCar.Clear();
        }

        // fell through the ground (or off the world): back next to the player
        static void RescueIfLost(Transform pl)
        {
            if (npc.transform.position.y > pl.position.y - 40f) return;
            float now = Time.time;
            if (now - firstRescue > 60f) { rescues = 0; firstRescue = now; }
            rescues++;
            Plugin.Log.LogWarning("Hire: " + displayName + " was " + (pl.position.y - npc.transform.position.y).ToString("0") + " m below you; bringing it back (" + rescues + ").");
            LogBody("fell");
            if (rescues >= 3)
            {
                // it keeps falling: end the hire instead of letting it die (no rep lost)
                string n = displayName;
                ReleaseNpc();
                Clear();
                toast(n + " got lost on the way. The hire is over and you keep your reputation.");
                return;
            }
            TeleportNear(pl);
        }

        // one line about the body, for the log when something goes wrong
        static void LogBody(string what)
        {
            try
            {
                Rigidbody rb = npc.GetComponent<Rigidbody>();
                int on = 0, off = 0, trig = 0;
                foreach (Collider c in npc.GetComponentsInChildren<Collider>(true)) { if (c.enabled) on++; else off++; if (c.isTrigger) trig++; }
                Rigidbody[] rbs = npc.GetComponentsInChildren<Rigidbody>(true);
                RaycastHit h; bool g = Physics.Raycast(npc.transform.position + Vector3.up * 2f, Vector3.down, out h, 200f, ~0, QueryTriggerInteraction.Ignore);
                Plugin.Log.LogInfo("Hire: body (" + what + "): pos " + npc.transform.position.ToString("0.0") + ", layer " + npc.layer + ", parent " + (npc.transform.parent != null ? npc.transform.parent.name : "none")
                    + ", rb " + (rb == null ? "none" : "kin=" + rb.isKinematic + " grav=" + rb.useGravity + " detect=" + rb.detectCollisions + " mode=" + rb.collisionDetectionMode + " vel=" + rb.velocity.ToString("0.0"))
                    + ", rigidbodies " + rbs.Length + ", colliders on/off/trigger " + on + "/" + off + "/" + trig
                    + ", ground below " + (g ? h.collider.name + " at " + h.point.y.ToString("0.0") : "none"));
            }
            catch (Exception e) { Plugin.Log.LogInfo("Hire: LogBody failed: " + e.Message); }
        }

        static void TeleportNear(Transform pl)
        {
            Vector3 spot = FindExit(pl, null);
            npc.transform.position = spot;
            LiftAbove(spot.y - 0.15f);
            Rigidbody rb = npc.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }

        // ---- the car -----------------------------------------------------------------------------------------------
        // The player's FSM "InCar": state "InCar" with the car in its variable "Car" (the same thing Apocapatrol reads).
        static GameObject PlayerCar(Transform pl, out bool inCar)
        {
            inCar = false;
            if (pl != null && Multiplayer.RemoteTargetable(pl.gameObject))
            { var remote = Multiplayer.RemoteCar(pl.gameObject); inCar = remote != null; return remote; }
            if (inCarFsm == null)
            {
                if (Time.unscaledTime < nextCarFind) return null;
                nextCarFind = Time.unscaledTime + 2f;
                PlayMakerFSM[] all = pl.root.GetComponentsInChildren<PlayMakerFSM>(true);
                for (int i = 0; i < all.Length; i++) if (all[i] != null && all[i].FsmName == "InCar") { inCarFsm = all[i]; break; }
                if (inCarFsm == null) return null;
            }
            if (inCarFsm.Fsm == null || !inCarFsm.Fsm.Initialized || inCarFsm.ActiveStateName != "InCar") return null;
            FsmGameObject v = inCarFsm.FsmVariables.GetFsmGameObject("Car");
            GameObject car = v != null ? v.Value : null;
            if (car == null) return null;
            inCar = true;
            return car;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            var queue = new Queue<Transform>();
            queue.Enqueue(root);
            int guard = 0;
            while (queue.Count > 0 && guard++ < 4000)
            {
                Transform t = queue.Dequeue();
                if (t.name == name) return t;
                for (int i = 0; i < t.childCount; i++) queue.Enqueue(t.GetChild(i));
            }
            return null;
        }

        // is something already sitting in the passenger seat (Apocapatrol's crews use a child called Apocapatrol.PassengerPos)?
        static bool SeatTaken(Transform car)
        {
            var queue = new Queue<Transform>();
            queue.Enqueue(car);
            int guard = 0;
            while (queue.Count > 0 && guard++ < 4000)
            {
                Transform t = queue.Dequeue();
                if ((t.name == "Apocapatrol.PassengerPos" || t.name == "ApocalypterQuests.HireSeat") && t.childCount > 0) return true;
                for (int i = 0; i < t.childCount; i++) queue.Enqueue(t.GetChild(i));
            }
            return false;
        }

        // Apocapatrol's arm poses (aiming / shooting) exist only for its own humans: lend a follower a copy of one of them


        static bool RangedUncached(GameObject go)
        {
            foreach (PlayMakerFSM f in QuestWorld.Fsms(go))
                if (f != null && (f.FsmName == "RangedAttackWait" || f.FsmName == "Damage Ranged")) return true;
            if (FindDeep(go.transform, "AttackRaycast_Ranged") != null) return true;
            return NamedRanged(go);
        }

        static bool NamedRanged(GameObject go)
        {
            string n = Game.BaseName(go.name);
            foreach (string part in (rangedNames.Value ?? "").Split(','))
            {
                string q = part.Trim();
                if (q.Length > 0 && n.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        // Apocapatrol decides who counts as a ranged human from its own list of names; add ours to it



        // Apocapatrol's [Apocapatrol] ShootDistance: occupants only shoot at targets closer than this
        static float PatrolShootDistance() { return Services.FollowerVehicles != null ? Services.FollowerVehicles.ShootDistance : 60f; }

        // The follower's own sensors look for the player's enemies the wrong way round, so while it rides the mod writes the target
        // it chose into the variable the game's Detection and Attack FSMs use (detectedObj); Apocapatrol's gunner reads it from there.

        // a few lines about what the gunner thinks, for the log (it is private, so by reflection)
        static void LogGuard() { }

        static float nextShot, realFireAt = -100f; static int shotsFired;
        static int magLeft = -1; internal static int magSize; internal static float reloadUntil; static float reloadSecs = 3f;

        static float NamedValue(string list, string name, float fallback)
        {
            foreach (string part in (list ?? "").Split(','))
            {
                string[] kv = part.Split('=');
                float v;
                if (kv.Length == 2 && name.IndexOf(kv[0].Trim(), StringComparison.OrdinalIgnoreCase) >= 0 && kv[0].Trim().Length > 0
                    && float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            }
            return fallback;
        }

        static bool Reloading { get { return Time.time < reloadUntil; } }

        static readonly List<AudioClip> reloadClips = new List<AudioClip>();
        static readonly List<KeyValuePair<float, AudioClip>> soundQueue = new List<KeyValuePair<float, AudioClip>>();

        // The player's own copy of each gun (under the camera's WeaponsArm) has an AnimSound FSM with its reload sounds
        // (mag out, mag in, slide...). Find the one that matches the gun in his hand and borrow its clips.
        static void FindReloadClips()
        {
            try
            {
                Transform rack = WeaponRack();
                if (rack == null) { Plugin.Log.LogInfo("Hire: the player's weapon rack was not found; " + displayName + " keeps the gun data he had."); return; }
                reloadClips.Clear();
                var held = new List<string>();
                if (gunHand != null)
                    for (int i = 0; i < gunHand.childCount; i++)
                    {
                        Transform c = gunHand.GetChild(i);
                        if (c.gameObject.activeInHierarchy && c.GetComponentInChildren<Renderer>() != null) held.Add(c.name.ToLowerInvariant().Replace("_model", ""));
                    }
                Transform best = null; bool viaFallback = false;
                for (int i = 0; i < rack.childCount; i++)
                {
                    Transform w = rack.GetChild(i);
                    string wn = w.name.ToLowerInvariant();
                    foreach (string h in held)
                        if ((h == wn || h.Contains(wn)) && (best == null || wn.Length > best.name.Length)) best = w;
                }
                if (best == null)
                {
                    string want = "";
                    foreach (string part in (reloadSoundFrom.Value ?? "").Split(','))
                    {
                        string[] kv = part.Split('=');
                        if (kv.Length == 2 && kv[0].Trim().Length > 0 && Game.BaseName(npc.name).IndexOf(kv[0].Trim(), StringComparison.OrdinalIgnoreCase) >= 0) want = kv[1].Trim();
                    }
                    if (want.Length > 0) { best = rack.Find(want); viaFallback = best != null; }
                }
                if (best == null) { Plugin.Log.LogInfo("Hire: " + displayName + "'s gun (" + string.Join(", ", held.ToArray()) + ") has no player version; no reload sounds (set ReloadSoundFrom)."); return; }
                ReadGunStats(best, rack);
                if (viaFallback || FindFsm(npc, "WeaponType") == null) BorrowShotSound(best);     // Bolter has no WeaponType: the game never set his gun's sound
                PlayMakerFSM snd = null;
                foreach (PlayMakerFSM f in best.GetComponents<PlayMakerFSM>()) if (f != null && f.FsmName == "AnimSound") { snd = f; break; }
                if (snd == null) { Plugin.Log.LogInfo("Hire: " + best.name + " has no AnimSound; no reload sounds."); return; }
                var states = new List<FsmState>(snd.FsmStates);
                states.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
                foreach (FsmState st in states)
                {
                    if (!st.Name.StartsWith("sound")) continue;
                    foreach (FsmStateAction a in st.Actions)
                    {
                        if (a == null) continue;
                        AudioClip clip = null;
                        foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                        {
                            object v = fi.GetValue(a);
                            if (v is AudioClip) clip = (AudioClip)v;
                            else if (v is FsmObject && ((FsmObject)v).Value is AudioClip) clip = (AudioClip)((FsmObject)v).Value;
                            if (clip != null) break;
                        }
                        if (clip != null) { reloadClips.Add(clip); break; }
                    }
                }
                var names = new List<string>(); foreach (AudioClip c in reloadClips) names.Add(c.name);
                Plugin.Log.LogInfo("Hire: " + displayName + " reloads with the sounds of " + best.name + ": " + string.Join(", ", names.ToArray())
                    + (gunPerRound && reloadClips.Count > 0 ? " (" + reloadClips[InsertClip()].name + " for every round)" : "") + ".");
            }
            catch (Exception e) { Plugin.LogOnce("HireReloadClips", e); }
        }

        // Magazine size and reload time from the player's copy of the gun:
        //  - Reload FSM, state checkAmmo: the first IntCompare tests the rounds against a fixed number (29 for a 30-round AKM,
        //    4 for a 5-shot revolver; a crossbow compares the other way round, 1 against the bolts it has)
        //  - a Reload FSM with "checkAmmoInStore" loads one round at a time (revolver, shotguns); otherwise one magazine
        //  - the time is the length of the reload clips in the gun's Animator
        static void ReadGunStats(Transform gun, Transform rack)
        {
            try
            {
                gunPerRound = false;
                PlayMakerFSM rl = null;
                foreach (PlayMakerFSM f in gun.GetComponents<PlayMakerFSM>()) if (f != null && f.FsmName == "Reload") { rl = f; break; }
                if (rl == null) { Plugin.Log.LogInfo("Hire: " + gun.name + " has no Reload FSM; magazine and reload time not known."); return; }
                int mag = 0; bool perRound = false;
                foreach (FsmState st in rl.FsmStates)
                {
                    if (st.Name == "checkAmmoInStore") perRound = true;
                    if (st.Name != "checkAmmo" || mag > 0) continue;
                    foreach (FsmStateAction a in st.Actions)
                    {
                        if (a == null || a.GetType().Name != "IntCompare") continue;
                        FsmInt i1 = a.GetType().GetField("integer1").GetValue(a) as FsmInt;
                        FsmInt i2 = a.GetType().GetField("integer2").GetValue(a) as FsmInt;
                        if (i1 == null || i2 == null) continue;
                        bool v1 = !string.IsNullOrEmpty(i1.Name), v2 = !string.IsNullOrEmpty(i2.Name);   // a named value is a variable
                        if (!v2 && v1) mag = i2.Value + 1;                          // rounds > 29: full
                        else if (!v1 && v2) mag = Math.Max(1, i1.Value);            // crossbow: 1 vs bolts
                        else mag = Math.Max(i1.Value, i2.Value) + 1;
                        break;
                    }
                }
                // reload clip lengths: the gun's own Animator, else the arms' Animator clips that name the gun
                var lens = new List<float>(); var clipNames = new List<string>();
                var anims = new List<Animator>();
                anims.AddRange(gun.GetComponentsInChildren<Animator>(true));
                bool own = anims.Count > 0;
                if (!own) { Animator ra = rack.GetComponent<Animator>() ?? (rack.parent != null ? rack.parent.GetComponent<Animator>() : null); if (ra != null) anims.Add(ra); }
                foreach (Animator an in anims)
                {
                    if (an == null || an.runtimeAnimatorController == null) continue;
                    foreach (AnimationClip c in an.runtimeAnimatorController.animationClips)
                    {
                        if (c == null) continue;
                        string cn = c.name.ToLowerInvariant();
                        if (!cn.Contains("reload")) continue;
                        if (!own && !cn.Contains(gun.name.ToLowerInvariant())) continue;
                        if (clipNames.Contains(c.name)) continue;
                        clipNames.Add(c.name); lens.Add(c.length);
                    }
                }
                if (lens.Count == 0)
                {
                    var all = new List<string>();
                    foreach (Animator an in anims) if (an != null && an.runtimeAnimatorController != null) foreach (AnimationClip c in an.runtimeAnimatorController.animationClips) if (c != null) all.Add(c.name + " " + c.length.ToString("0.0"));
                    Plugin.Log.LogInfo("Hire: " + gun.name + " has no clip named reload; its clips: " + string.Join(", ", all.ToArray()));
                }
                float secs = -1f;
                if (lens.Count > 0)
                {
                    float sum = 0f, min = float.MaxValue, max = 0f;
                    foreach (float l in lens) { sum += l; min = Mathf.Min(min, l); max = Mathf.Max(max, l); }
                    secs = perRound ? sum + min * Mathf.Max(0, mag - 1) : max;          // one round at a time: every round goes in
                    secs = Mathf.Clamp(secs, 0.5f, 10f);
                }
                gunPerRound = perRound;
                if (mag > 0) magSize = mag;
                if (secs > 0f) reloadSecs = secs;
                Plugin.Log.LogInfo("Hire: " + gun.name + ": " + (mag > 0 ? mag + " rounds" : "rounds unknown") + (perRound ? ", loaded one at a time" : "") + ", reload clips " + (clipNames.Count > 0 ? string.Join(", ", clipNames.ToArray()) : "none") + (secs > 0f ? " = " + secs.ToString("0.0") + " s" : "") + ".");
            }
            catch (Exception e) { Plugin.LogOnce("HireGunStats", e); }
        }

        static bool gunPerRound;

        // Turn in the seat to face the target (only around the vertical); back to the seat's front when there is none.
        // The game's own Attack FSM does this for most of them, but not for all (Bolter), and not while reloading.
        static Quaternion seatBase = Quaternion.identity;

        static void TurnToTarget()
        {
            if (npc == null || mountObj == null || mountObj.transform.parent == null) return;
            try
            {
                // turn the seat itself: Apocapatrol's pose sits him on it and its firing arc is measured from it, so he turns and can fire to the side
                Transform car = mountObj.transform.parent;
                Quaternion want = seatBase;
                if (target != null && Alive(target))
                {
                    Vector3 local = car.InverseTransformDirection(target.transform.position - mountObj.transform.position);
                    Vector3 baseFwd = seatBase * Vector3.forward;
                    float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg - Mathf.Atan2(baseFwd.x, baseFwd.z) * Mathf.Rad2Deg;
                    yaw = Mathf.DeltaAngle(0f, yaw);
                    yaw = Mathf.Clamp(yaw, -150f, 150f);
                    want = Quaternion.AngleAxis(yaw, Vector3.up) * seatBase;
                }
                mountObj.transform.localRotation = Quaternion.RotateTowards(mountObj.transform.localRotation, want, 200f * Time.deltaTime);
            }
            catch (Exception e) { Plugin.LogOnce("HireTurn", e); }
        }

        // A magazine gun plays its sounds once, spread over the reload. A gun loaded one round at a time (revolver, shotguns)
        // plays the sounds before the "insert" sound once, the insert sound for every round, then the rest once.
        // his gun is not one the game gave him a sound for (Bolter's crossbow plays a gunshot): take the shot sound of the
        // player's version, which sits on the effect its Attack FSM spawns when it fires (GunShot_crossbow_Effect)
        static void BorrowShotSound(Transform gun)
        {
            try
            {
                AudioClip clip = null;
                foreach (PlayMakerFSM f in gun.GetComponents<PlayMakerFSM>())
                {
                    if (f == null || f.FsmName != "Attack") continue;
                    foreach (FsmState st in f.FsmStates)
                    {
                        if (st.Name != "fire" && st.Name != "shoot") continue;
                        foreach (FsmStateAction a in st.Actions)
                        {
                            if (a == null || a.GetType().Name != "CreateObject") continue;
                            FieldInfo fi = a.GetType().GetField("gameObject");
                            FsmGameObject go = fi != null ? fi.GetValue(a) as FsmGameObject : null;
                            AudioSource src = go != null && go.Value != null ? go.Value.GetComponentInChildren<AudioSource>(true) : null;
                            if (src != null && src.clip != null) { clip = src.clip; break; }
                        }
                        if (clip != null) break;
                    }
                }
                if (clip == null) { Plugin.Log.LogInfo("Hire: no shot sound found on " + gun.name + "; " + displayName + " keeps his own."); return; }
                PlayMakerFSM snd = FindFsm(npc, "Sound2");
                if (snd == null) return;
                FsmObject gs = snd.FsmVariables.GetFsmObject("GunSound");
                if (gs != null) gs.Value = clip;
                foreach (FsmState st in snd.FsmStates)
                    foreach (FsmStateAction a in st.Actions)
                    {
                        if (a == null || a.GetType().Name != "SetAudioClip") continue;
                        FieldInfo fi = a.GetType().GetField("audioClip");
                        FsmObject v = fi != null ? fi.GetValue(a) as FsmObject : null;
                        if (v != null) v.Value = clip;
                    }
                Plugin.Log.LogInfo("Hire: " + displayName + " shoots with the sound of " + gun.name + " (" + clip.name + ").");
            }
            catch (Exception e) { Plugin.LogOnce("HireShotSound", e); }
        }

        static Transform rackCache;

        // the player's own guns: Camera/WeaponsArm/Parent. Found from the camera first (the player's root can be the car or a big
        // scene object, too large to search), then from the player, and kept once found.
        static Transform WeaponRack()
        {
            if (rackCache != null) return rackCache;
            Transform arm = null;
            Camera cam = GameRefs.MainCamera;
            if (cam != null) arm = FindDeep(cam.transform, "WeaponsArm");
            if (arm == null) { GameObject g = GameRefs.PlayerCameraHolder != null ? GameRefs.PlayerCameraHolder : null; if (g != null) arm = g.transform; }
            if (arm == null) { Transform pl = PartyPlayer(); if (pl != null) arm = FindDeep(pl, "WeaponsArm") ?? FindDeep(pl.root, "WeaponsArm"); }
            Transform rack = arm != null ? (arm.Find("Parent") ?? FindDeep(arm, "Parent")) : null;
            if (rack != null) rackCache = rack;
            return rack;
        }

        static void ScheduleReloadSounds()
        {
            soundQueue.Clear();
            int n = reloadClips.Count;
            if (n == 0) return;
            float start = Time.time + Mathf.Min(0.25f, reloadSecs * 0.1f), end = reloadUntil - Mathf.Min(0.35f, reloadSecs * 0.15f);
            if (!gunPerRound || magSize <= 1)
            {
                for (int i = 0; i < n; i++)
                    soundQueue.Add(new KeyValuePair<float, AudioClip>(n == 1 ? start : Mathf.Lerp(start, end, i / (float)(n - 1)), reloadClips[i]));
                return;
            }
            int ins = InsertClip();
            var seq = new List<AudioClip>();
            for (int i = 0; i < ins; i++) seq.Add(reloadClips[i]);
            for (int r = 0; r < magSize; r++) seq.Add(reloadClips[ins]);
            for (int i = ins + 1; i < n; i++) seq.Add(reloadClips[i]);
            for (int i = 0; i < seq.Count; i++)
                soundQueue.Add(new KeyValuePair<float, AudioClip>(seq.Count == 1 ? start : Mathf.Lerp(start, end, i / (float)(seq.Count - 1)), seq[i]));
        }

        // which of the gun's sounds is the round going in: by name if one says so, else the middle one
        static int InsertClip()
        {
            string[] keys = { "insert", "ammo_in", "bullet", "round", "load", "mag_in", "_in", "shell" };
            foreach (string k in keys)
                for (int i = 0; i < reloadClips.Count; i++)
                {
                    string cn = reloadClips[i] != null ? reloadClips[i].name.ToLowerInvariant() : "";
                    if (cn.Contains(k) && !cn.Contains("empty")) return i;      // an empty shell is the one coming out
                }
            return reloadClips.Count >= 3 ? reloadClips.Count / 2 : reloadClips.Count - 1;
        }

        static void PlayDueSounds()
        {
            if (soundQueue.Count == 0 || npc == null) return;
            for (int i = soundQueue.Count - 1; i >= 0; i--)
            {
                if (Time.time < soundQueue[i].Key) continue;
                AudioClip clip = soundQueue[i].Value;
                soundQueue.RemoveAt(i);
                if (clip == null) continue;
                try
                {
                    Transform s2 = FindDeep(npc.transform, "Sound2");
                    AudioSource src = s2 != null ? s2.GetComponent<AudioSource>() : null;
                    if (src != null) src.PlayOneShot(clip, 0.9f);
                    else AudioSource.PlayClipAtPoint(clip, npc.transform.position, 0.9f);
                }
                catch (Exception e) { Plugin.LogOnce("HireReloadSound", e); }
            }
        }

        // one shot gone, real or backup; an empty magazine means a pause to reload
        static void CountShot()
        {
            if (magSize > 0)
            {
                if (magLeft < 0) magLeft = magSize;
                if (--magLeft <= 0)
                {
                    magLeft = magSize; reloadUntil = Time.time + reloadSecs; ScheduleReloadSounds();
                    Plugin.Log.LogInfo("Hire: " + displayName + " reloads (" + reloadSecs.ToString("0.#") + " s).");
                }
            }
            QuestFollower.Shot();
        }

        // while reloading, Apocapatrol's gunner is told it cannot fire, so his own Attack FSM stays off and the arms come down
        static void HoldFire() { if (mounted) FeedTarget(); }

        // The follower's own sensors only look for the player, so the Attack/Damage Ranged FSMs never get a hit on one of your enemies.
        // While the gunner says it can fire, the mod lowers the target's Health the way the game's own Damage Ranged does (Bodypart -> Health).
        static void FireShots()
        {
            if (npc == null || guardComp == null || target == null || !Alive(target) || shotDamage.Value <= 0f || Reloading) return;
            float now = Time.time;
            if (now < nextShot) return;
            if (!ClearShot()) return;                            // a wall, hull or rock between them: no backup shot through it
            if (realFire.Value)
            {
                foreach (PlayMakerFSM f in QuestWorld.Fsms(npc))
                {
                    if (f == null || !f.enabled) continue;
                    if ((f.FsmName == "Attack" && f.ActiveStateName == "attack_ranged") || (f.FsmName == "Damage Ranged" && f.ActiveStateName == "attack")) realFireAt = now;
                }
                if (now - realFireAt < 4f) return;                // its own shooting works; the backup stays out of it
            }
            try
            {
                bool can;
                can = Services.FollowerVehicles != null && Services.FollowerVehicles.CanFire(npc);   // the gunner did not take him as ranged: the mod judges range itself
                if (!can) return;
                nextShot = now + Mathf.Max(0.2f, shotInterval.Value) * UnityEngine.Random.Range(0.8f, 1.2f);
                PlayMakerFSM hf = FindFsm(target, "Health");
                FsmFloat hp = hf != null ? hf.FsmVariables.GetFsmFloat("Health") : null;
                if (hp == null) { Plugin.LogOnce("HireShotNoHealth", new Exception("target " + target.name + " has no Health variable")); return; }
                hp.Value -= ownDamage > 0f ? ownDamage : shotDamage.Value;     // his own weapon's damage when it can be read (Bolter hits harder)
                shotsFired++;
                if (shotsFired <= 6) Plugin.Log.LogInfo("Hire: " + displayName + " shoots " + target.name + " (health now " + hp.Value.ToString("0") + ").");
                PlayShotAnim();
                CountShot();
                PlayMakerFSM snd = FindFsm(npc, "Sound2");
                if (snd != null) { selfShotAt = Time.time; try { snd.Fsm.SetState("playAudio"); } catch (Exception) { } }
            }
            catch (Exception e) { Plugin.LogOnce("HireShoot", e); }
        }

        static Animator npcAnim; static float animBack; static string lastAtkState;

        // The shooting animation (muzzle flash, recoil, reload) is played by the Movement FSM, which is muted while he rides.
        // Play the same Animator state ("attack 2") on every shot, then go back to idle.
        static List<GameObject> flashObjs; static float flashOff;

        // his parts, once per ride: the muzzle flash objects, and the bones of the arm that holds the gun (for the recoil)
        static void ScanParts()
        {
            try
            {
                flashObjs = new List<GameObject>();
                var names = new System.Text.StringBuilder();
                foreach (Transform t in npc.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.ToLowerInvariant();
                    if (names.Length < 1500) names.Append(t.name).Append(t.gameObject.activeSelf ? "" : "(off)").Append(", ");
                    // each gun carries a "fire_effect" (off) with the flash parts under it; the shoot clip switches it on for a moment
                    bool flashName = n.StartsWith("fire_effect") || n.Contains("muzzle") || n.Contains("flash");
                    bool parentToo = t.parent != null && t.parent.name.ToLowerInvariant().StartsWith("fire_effect");
                    if (flashName && !parentToo) flashObjs.Add(t.gameObject);
                }
                Plugin.Log.LogInfo("Hire: " + displayName + " parts: " + names);
                Plugin.Log.LogInfo("Hire: " + displayName + " muzzle flash objects found: " + flashObjs.Count);
                Animator a0 = npc.GetComponent<Animator>() ?? npc.GetComponentInChildren<Animator>(true);
                if (a0 != null && a0.runtimeAnimatorController != null)
                {
                    var cn = new System.Text.StringBuilder();
                    foreach (AnimationClip c in a0.runtimeAnimatorController.animationClips) cn.Append(c.name).Append(", ");
                    Plugin.Log.LogInfo("Hire: " + displayName + " animation clips: " + cn);
                }
                gunArm = null; gunHand = null;
                foreach (GameObject g in flashObjs)
                {
                    if (g == null || g.transform.parent == null || !g.transform.parent.gameObject.activeInHierarchy) continue;
                    for (Transform h = g.transform.parent; h != null && h != npc.transform; h = h.parent)
                        if (h.name.EndsWith("Hand")) { gunHand = h; gunArm = h.parent; break; }
                    if (gunHand != null) break;
                }
                if (gunHand == null)
                {
                    // no flash part (Bolter): take the hand that has a visible weapon model under it
                    foreach (Transform h in npc.GetComponentsInChildren<Transform>(true))
                    {
                        if (!h.name.EndsWith("Hand") || h.parent == null) continue;
                        foreach (Renderer r in h.GetComponentsInChildren<Renderer>(false))
                            if (r != null && r.enabled && r.transform != h) { gunHand = h; gunArm = h.parent; break; }
                        if (gunHand != null) break;
                    }
                }
                chestBone = null; kickBones.Clear();
                foreach (Transform b in npc.GetComponentsInChildren<Transform>(true))
                {
                    if (b.name.EndsWith("Spine2")) chestBone = b;
                    else if (b.name.EndsWith("LeftForeArm") || b.name.EndsWith("RightForeArm")) kickBones.Add(b);
                }
                if (kickBones.Count == 0 && gunArm != null) kickBones.Add(gunArm);
                ownDamage = 0f;
                PlayMakerFSM dr = FindFsm(npc, "Damage Ranged");
                FsmFloat dv = dr != null ? dr.FsmVariables.GetFsmFloat("Damage Value") : null;
                if (dv != null && Mathf.Abs(dv.Value) > 0.1f) ownDamage = Mathf.Abs(dv.Value);
                FindReloadClips();
                Plugin.Log.LogInfo("Hire: " + displayName + " holds his gun in " + (gunHand != null ? gunHand.name : "(no hand found)") + ", shot damage " + (ownDamage > 0f ? ownDamage.ToString("0") + " (his own)" : shotDamage.Value.ToString("0") + " (ShotDamage)") + ".");
            }
            catch (Exception e) { Plugin.LogOnce("HireScanParts", e); if (flashObjs == null) flashObjs = new List<GameObject>(); }
        }

        static Transform gunArm, gunHand, chestBone; static readonly List<Transform> kickBones = new List<Transform>(); static float recoilAt = -10f, ownDamage;

        // a short kick of the gun arm, drawn after Apocapatrol's pose (onBeforeRender comes after every LateUpdate)
        static void Recoil()
        {
            try
            {
                if (npc == null || kickBones.Count == 0 || recoilDeg == null || recoilDeg.Value <= 0f) return;
                float t = Time.time - recoilAt;
                const float up = 0.04f, dur = 0.2f;
                if (t < 0f || t > dur) return;
                float k = t < up ? t / up : 1f - (t - up) / (dur - up);
                Vector3 axis = npc.transform.right;
                float a = recoilDeg.Value * k;
                // the chest rocks back a little and both forearms kick up, so the gun and both hands move together
                if (chestBone != null) chestBone.rotation = Quaternion.AngleAxis(-a * 0.25f, axis) * chestBone.rotation;
                foreach (Transform b in kickBones) if (b != null) b.rotation = Quaternion.AngleAxis(-a, axis) * b.rotation;
            }
            catch (Exception e) { Plugin.LogOnce("HireRecoil", e); }
        }

        static void PlayShotAnim()
        {
            try
            {
                if (flashObjs == null) ScanParts();
                foreach (GameObject g in flashObjs)
                    if (g != null && g.transform.parent != null && g.transform.parent.gameObject.activeInHierarchy) { g.SetActive(false); g.SetActive(true); }   // only the gun he holds
                flashOff = Time.time + 0.1f;
                recoilAt = Time.time;
                if (shotAnim.Value)
                {
                    if (npcAnim == null) npcAnim = npc.GetComponent<Animator>() ?? npc.GetComponentInChildren<Animator>(true);
                    if (npcAnim != null) { QuestFollower.Play(npcAnim, "attack 2", 0, 0f); animBack = Time.time + 0.9f; }
                }
            }
            catch (Exception e) { Plugin.LogOnce("HireShotAnim", e); }
        }

        static void WatchShots()
        {
            if (npc == null) return;
            try
            {
                string st = null;
                foreach (PlayMakerFSM f in QuestWorld.Fsms(npc))
                    if (f != null && f.enabled && f.FsmName == "Attack") { st = f.ActiveStateName; break; }
                if (st == "attack_ranged" && lastAtkState != "attack_ranged") realFireAt = Time.time;
                if (RealShotFired()) { realFireAt = Time.time; PlayShotAnim(); CountShot(); }
                lastAtkState = st;
                if (flashOff > 0f && Time.time > flashOff) { flashOff = 0f; if (flashObjs != null) foreach (GameObject g in flashObjs) if (g != null) g.SetActive(false); }
                if (animBack > 0f && Time.time > animBack && npcAnim != null) { animBack = 0f; QuestFollower.Play(npcAnim, "idle", 0, 0f); }
            }
            catch (Exception e) { Plugin.LogOnce("HireWatchShots", e); }
        }


        static void FeedTarget() { if (Services.FollowerVehicles != null) Services.FollowerVehicles.Combat(npc, target, reloadUntil); }

        static GameObject madeRaycast;

        // Apocapatrol changes the passenger for good: it rewires the events in his Attack FSM to ranged only, switches off the
        // actions of his Damage/FireDamage FSMs (melee and contact damage) and those FSMs themselves. Nothing puts that back when
        // its gunner is removed, so after the car he followed but no longer fought. All of it is recorded before and restored after.
        static readonly List<KeyValuePair<FsmStateAction, bool>> actSnap = new List<KeyValuePair<FsmStateAction, bool>>();
        static readonly List<object[]> evtSnap = new List<object[]>();          // { action, FieldInfo, value }
        static readonly List<KeyValuePair<PlayMakerFSM, bool>> preGuardFsms = new List<KeyValuePair<PlayMakerFSM, bool>>();

        static void SnapCombat()
        {
            actSnap.Clear(); evtSnap.Clear(); preGuardFsms.Clear();
            try
            {
                foreach (PlayMakerFSM f in QuestWorld.Fsms(npc))
                {
                    if (f == null) continue;
                    preGuardFsms.Add(new KeyValuePair<PlayMakerFSM, bool>(f, f.enabled));
                    if (f.FsmName != "Attack" && f.FsmName != "Damage" && f.FsmName != "FireDamage" && f.FsmName != "Damage Ranged") continue;
                    foreach (FsmState st in f.FsmStates)
                        foreach (FsmStateAction a in st.Actions)
                        {
                            if (a == null) continue;
                            actSnap.Add(new KeyValuePair<FsmStateAction, bool>(a, a.Enabled));
                            foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                                if (fi.FieldType == typeof(FsmEvent)) evtSnap.Add(new object[] { a, fi, fi.GetValue(a) });
                        }
                }
            }
            catch (Exception e) { Plugin.LogOnce("HireSnapCombat", e); }
        }

        static void RestoreCombat()
        {
            int n = 0;
            try
            {
                foreach (var kv in actSnap) if (kv.Key != null && kv.Key.Enabled != kv.Value) { kv.Key.Enabled = kv.Value; n++; }
                foreach (object[] e in evtSnap)
                {
                    FieldInfo fi = (FieldInfo)e[1];
                    if (!object.Equals(fi.GetValue(e[0]), e[2])) { fi.SetValue(e[0], e[2]); n++; }
                }
                foreach (var kv in preGuardFsms)
                    if (kv.Key != null && (kv.Key.FsmName == "Damage" || kv.Key.FsmName == "FireDamage") && kv.Key.enabled != kv.Value) { kv.Key.enabled = kv.Value; n++; }
                if (n > 0) Plugin.Log.LogInfo("Hire: " + displayName + "'s melee and attack wiring put back as before the car (" + n + " changes undone).");
            }
            catch (Exception ex) { Plugin.LogOnce("HireRestoreCombat", ex); }
            actSnap.Clear(); evtSnap.Clear(); preGuardFsms.Clear();
        }

        // a fresh start for his fighting AI on foot: forget the car's target and let Detection look again
        static void ResetFight()
        {
            try
            {
                foreach (PlayMakerFSM f in QuestWorld.Fsms(npc))
                {
                    if (f == null) continue;
                    FsmGameObject v = f.FsmVariables.GetFsmGameObject("detectedObj");
                    if (v != null) v.Value = null;
                    if (f.FsmName == "Detection") { f.enabled = true; try { f.Fsm.SetState("notDetected"); } catch (Exception) { } }
                }
            }
            catch (Exception e) { Plugin.LogOnce("HireResetFight", e); }
        }
        public static void EarlyTeach() { }

        // Apocapatrol's list of ranged humans is read by code that Mono compiles once; a name added after that is never seen.
        // So every hirable name goes in at the start, before any car crew makes Apocapatrol use the list.


        // if Apocapatrol still did not take him as a shooter, switch its gunner over by hand (it turns combat on itself the next frame)


        static Component AttachGuard(GameObject car)
        {
            if (Services.FollowerVehicles == null) return null;
            if (FindDeep(npc.transform, "AttackRaycast_Ranged") == null)
            {
                madeRaycast = new GameObject("AttackRaycast_Ranged"); madeRaycast.transform.SetParent(npc.transform, false);
                madeRaycast.transform.localPosition = new Vector3(0, 1.4f, .4f);
            }
            SnapCombat();
            var guard = Services.FollowerVehicles.SeatCombat(npc, car, mountObj.transform);
            GunSetup(); return guard;
        }

        // his gun: flash parts, gun hand, damage, magazine, reload time and sounds (car and foot alike)
        static void GunSetup()
        {
            int prevMag = magSize; float prevReload = reloadSecs;
            magSize = 0; reloadSecs = 3f;
            ScanParts();
            if (magSize <= 0 && prevMag > 0) { magSize = prevMag; reloadSecs = prevReload; }   // could not read his gun this time: keep what he had
            string bn = Game.BaseName(npc.name);
            float mo = NamedValue(magSizes.Value, bn, -1f), ro = NamedValue(reloadTimes.Value, bn, -1f);
            if (mo >= 0f) magSize = Mathf.RoundToInt(mo);
            if (ro > 0f) reloadSecs = Mathf.Max(0.2f, ro);
            if (magLeft < 0 || magLeft > magSize) magLeft = magSize;
            Plugin.Log.LogInfo("Hire: " + displayName + " magazine " + (magSize > 0 ? magSize.ToString() : "none (never reloads)") + ", reload " + reloadSecs.ToString("0.0") + " s" + (mo >= 0f || ro > 0f ? " (override)" : "") + ".");
        }

        static void DetachGuard()
        {
            if (flashObjs != null) foreach (GameObject g in flashObjs) if (g != null) g.SetActive(false);
            flashOff = 0f; gunArm = null; gunHand = null; soundQueue.Clear();
            if (madeRaycast != null) { UnityEngine.Object.Destroy(madeRaycast); madeRaycast = null; }
            if (guardComp == null) return;
            Behaviour gb = guardComp as Behaviour;
            if (gb != null) gb.enabled = false;           // Destroy waits for the end of the frame; its Update must not mute his AI again before that
            UnityEngine.Object.Destroy(guardComp);
            RestoreCombat();
            guardComp = null;
            foreach (var kv in rendSnap) if (kv.Key != null) kv.Key.enabled = kv.Value;
            foreach (var kv in colSnap) if (kv.Key != null) kv.Key.enabled = kv.Value;
            rendSnap.Clear(); colSnap.Clear();
        }

        static Animator seatAnim; static bool wasRootMotion; static float nextColCheck;

        // any collider of his that came on after he sat down (weapons, the fire-damage box...) must not touch the car either
        static void IgnoreNewColliders()
        {
            if (npc == null) return;
            try
            {
                foreach (Collider c in npc.GetComponentsInChildren<Collider>(false))
                {
                    if (c == null || !c.enabled || ownCols.Contains(c)) continue;
                    ownCols.Add(c);
                    for (int k = 0; k < ignoredCars.Count; k++) if (ignoredCars[k] != null) Physics.IgnoreCollision(c, ignoredCars[k], true);
                }
            }
            catch (Exception e) { Plugin.LogOnce("HireIgnoreCols", e); }
        }

        static bool TryMount(GameObject car)
        {
            Transform ct = car.transform;
            Transform sit = FindDeep(ct, "sitPos"), hd = FindDeep(ct, "hinge_seat_driver"), hp = FindDeep(ct, "hinge_seat_passenger");
            if (sit == null || hd == null || hp == null) { Plugin.Log.LogInfo("Hire: " + car.name + " has no passenger seat; the follower stays out."); return false; }
            if (SeatTaken(ct)) { Plugin.Log.LogInfo("Hire: the passenger seat of " + car.name + " is taken; the follower stays out."); return false; }

            mountObj = new GameObject("ApocalypterQuests.HireSeat");
            mountObj.transform.SetParent(ct, false);
            mountObj.transform.position = sit.position + (hp.position - hd.position);
            mountObj.transform.rotation = sit.rotation;
            seatBase = mountObj.transform.localRotation;

            // stop him first: a running NPC keeps his run animation (and its root motion) and shoves the car about
            EndFootShot();
            if (moveFsm != null && moveFsm.ActiveStateName == "Run") { try { moveFsm.Fsm.SetState("Idle"); } catch (Exception) { } }
            seatAnim = npc.GetComponent<Animator>() ?? npc.GetComponentInChildren<Animator>(true);
            if (seatAnim != null) { wasRootMotion = seatAnim.applyRootMotion; seatAnim.applyRootMotion = false; try { seatAnim.Play("idle", 0, 0f); } catch (Exception) { } }

            // switch the NPC's own brain off while it sits
            Ranged.Clear(); _nearestName = null;
            muted.Clear();
            PlayMakerFSM[] fsms = QuestWorld.Fsms(npc);
            for (int i = 0; i < fsms.Length; i++)
            {
                if (fsms[i] == null || !fsms[i].enabled) continue;
                if (Array.IndexOf(MuteNames, fsms[i].FsmName) < 0) continue;
                fsms[i].enabled = false;
                muted.Add(fsms[i]);
            }
            Rigidbody rb = npc.GetComponent<Rigidbody>();
            wasKinematic = rb != null && rb.isKinematic;
            if (rb != null) { wasInterp = rb.interpolation; wasDetect = rb.collisionDetectionMode; wasGravity = rb.useGravity; }
            if (rb != null) { rb.velocity = Vector3.zero; rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; }

            // it must not shove the car around
            ownCols.Clear(); ignoredCars.Clear();
            foreach (Collider c in npc.GetComponentsInChildren<Collider>(false)) if (c != null && c.enabled) ownCols.Add(c);
            foreach (Collider c in car.GetComponentsInChildren<Collider>(false)) if (c != null && c.enabled) ignoredCars.Add(c);
            for (int i = 0; i < ownCols.Count; i++)
                for (int k = 0; k < ignoredCars.Count; k++)
                    if (ownCols[i] != null && ignoredCars[k] != null) Physics.IgnoreCollision(ownCols[i], ignoredCars[k], true);

            origParent = npc.transform.parent;
            npc.transform.position = mountObj.transform.position;
            npc.transform.rotation = mountObj.transform.rotation;
            npc.transform.SetParent(mountObj.transform, true);

            poseComp = null;
            if (Services.FollowerVehicles != null)
                poseComp = Services.FollowerVehicles.SeatPose(npc, mountObj.transform, Game.BaseName(npc.name), ArmTemplate(Game.BaseName(npc.name)));

            // ranged followers shoot from the seat with Apocapatrol's own passenger gunner
            guardComp = null;
            try
            {
                if (shootFromCar.Value && IsRangedNpc(npc)) guardComp = AttachGuard(car);
            }
            catch (Exception e) { Plugin.LogOnce("HireGuard", e.InnerException ?? e); guardComp = null; }

            IgnoreNewColliders();             // the gunner setup can switch some of his colliders back on
            mounted = true;
            carObj = car;
            target = null;
            Plugin.Log.LogInfo("Hire: " + displayName + " rides in " + car.name + ".");
            return true;
        }

        static void Dismount(bool place)
        {
            if (!mounted) return;
            mounted = false;
            outSince = -1f;
            GameObject carForExit = carObj;
            carObj = null;
            inCarSince = -1f;
            DetachGuard();
            footSince = -1f;                               // keeps the car's target: on foot he goes on shooting at it (FootWatch)
            Transform pl = PartyPlayer();
            try
            {
                if (poseComp != null)
                {
                    if (Services.FollowerVehicles != null) Services.FollowerVehicles.ReleasePose(poseComp);
                    UnityEngine.Object.Destroy(poseComp);
                }
            }
            catch (Exception) { }
            poseComp = null;

            if (npc != null)
            {
                // first find the spot (the car's colliders are still ignored, so nothing pushes the NPC while it moves)
                Vector3 spot = npc.transform.position;
                if (place && pl != null) spot = FindExit(pl, carForExit);
                npc.transform.SetParent(origParent, true);
                npc.transform.position = spot;
                npc.transform.rotation = Quaternion.Euler(0f, pl != null ? pl.eulerAngles.y : npc.transform.eulerAngles.y, 0f);
                if (place) LiftAbove(spot.y - 0.15f);
                Rigidbody rb = npc.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = wasKinematic;
                    rb.useGravity = wasGravity; rb.detectCollisions = true;
                    try { rb.collisionDetectionMode = wasKinematic ? CollisionDetectionMode.ContinuousSpeculative : wasDetect; rb.interpolation = wasInterp; } catch (Exception) { }
                    if (!wasKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.WakeUp(); }
                }
                foreach (Collider c in npc.GetComponentsInChildren<Collider>(true)) if (c != null && c.gameObject.activeInHierarchy && !c.enabled && ownCols.Contains(c)) c.enabled = true;
                Physics.SyncTransforms();
                LogBody("got out, placed at " + spot.ToString("0.0"));
                for (int i = 0; i < muted.Count; i++) if (muted[i] != null) muted[i].enabled = true;
                if (seatAnim != null) { seatAnim.applyRootMotion = wasRootMotion; seatAnim = null; }
                wakeLogged = false; WakeBrain(true);
                ResetFight();
                footSetup = false;
                settleUntil = Time.time + 2.5f;
                // the car stays "not solid" for this NPC for a few seconds more, in case it is still next to the doors
                lateOwn.Clear(); lateCar.Clear();
                lateOwn.AddRange(ownCols); lateCar.AddRange(ignoredCars);
                restoreAt = Time.time + 5f;
            }
            Ranged.Clear(); _nearestName = null;
            muted.Clear(); ignoredCars.Clear(); ownCols.Clear();
            if (mountObj != null) UnityEngine.Object.Destroy(mountObj);
            mountObj = null;
            Plugin.Log.LogInfo("Hire: " + displayName + " got out.");
        }

        // ---- the end -------------------------------------------------------------------------------------------------
        static void Died()
        {
            string n = displayName;
            string fac = book.HireFaction;
            if (mounted) Dismount(false);
            Clear();
            toast("Your hired help " + n + " was killed.");
            if (!string.IsNullOrEmpty(fac) && deathPenalty.Value > 0) changeRep(fac, -deathPenalty.Value, null);
        }

        // the object is gone: killed if it was close to you, otherwise it was just unloaded (the hire simply ends)
        static void Gone()
        {
            if (lastDistance < 80f && !wasMountedAtLoss) { Died(); return; }
            string n = displayName;
            if (mounted) Dismount(false);
            Clear();
            toast(n + " got lost. The hire is over.");
        }
        static string ArmTemplate(string name)
        {
            string like = "Lugnut";
            foreach (string part in (armPoseLike.Value ?? "").Split(','))
            {
                string p = part.Trim(); int eq = p.IndexOf('=');
                if (eq < 0) { if (p.Length > 0) like = p; }
                else if (string.Equals(p.Substring(0, eq).Trim(), name, StringComparison.OrdinalIgnoreCase)) return p.Substring(eq + 1).Trim();
            }
            return like;
        }
        internal static void Shutdown()
        {
            QuestFollower.Release();
            if (mounted) Dismount(true);
            ReleaseNpc(); RestoreCollisions(); Reset();
            if (recoilHooked) Application.onBeforeRender -= Recoil;
            recoilHooked = false; book = null; toast = null; changeRep = null;
        }
    }
}
