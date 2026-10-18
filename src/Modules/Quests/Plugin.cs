using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using WildWasteland.Core;
using WildWasteland.Core.UI;

namespace WildWasteland.Modules.Quests
{
    internal sealed partial class Plugin : EngineSettings
    {
        public const string PluginGuid = "local.apocalypterquests";
        public const string PluginName = "Apocalypter Quests";
        public const string PluginVersion = "1.8.2";
        public const string TalkGuid = "com.example.apocalypter.talk";
        const string SaveKey = "ApocalypterQuests";

        internal static Plugin I;
        internal static QuestLogger Log;
        static readonly HashSet<string> logged = new HashSet<string>();

        internal static void LogOnce(string where, Exception e)
        {
            if (Log != null && logged.Add(where)) Log.LogError(where + ": " + e);
        }

        // ---- config ----
        bool allowTakeover;
        string factionsPath;
        const int RepMax = 100;
        float CooldownHours { get { return giverCooldown.Value * 24f; } }
        bool allowDeliver;
        HashSet<string> blockedNow;

        // ---- quests ----
        internal readonly QuestBook book = new QuestBook();
        List<Quest> defs = new List<Quest>();
        readonly System.Random rng = new System.Random();
        readonly bool[] allowedGen = new bool[5];
        bool anyGen;

        // ---- Talk ----
        readonly TalkBridge bridge = new TalkBridge();
        Quest pendingOffer;
        int pendingFor;
        readonly Dictionary<int, Quest> offers = new Dictionary<int, Quest>();
        readonly Queue<Action> deferred = new Queue<Action>();

        // ---- save / load tracking ----
        bool ready, saveBlocked;
        Type es3Type;
        bool es3Resolved;
        MethodInfo es3Save, es3Load, es3Exists, es3SaveS, es3LoadS, es3ExistsS;
        Type es3SettingsType;
        object es3FileLocation;      // ES3.Location.File

        // ---- ui ----
        class Toast { public string Text; public float Until; }
        readonly List<Toast> toasts = new List<Toast>();
        readonly List<string> trackerLines = new List<string>();
        readonly List<string> logLines = new List<string>();
        string logText = "";        // the quest log's text, built once a second instead of on every GUI event
        bool showLog;
        static bool GamePaused() { return Time.timeScale < 0.01f || (Cursor.visible && Cursor.lockState != CursorLockMode.Locked); }
        float nextRefresh;
        GUIStyle trackStyle, toastStyle, panelStyle, titleStyle;

        // ------------------------------------------------------------------


        void ApplyTakeoverSettings()
        {
            Takeover.SpawnDistance = takeoverSpawnDist.Value;
            Takeover.GroupSize = takeoverGroup.Value;
            Takeover.WalkSeconds = takeoverWalk.Value;
            Takeover.GarrisonSize = garrisonSize.Value;
            Takeover.GarrisonTrigger = garrisonRange.Value;
            Takeover.GiverRespawnRange = giverRespawn.Value;
            Takeover.GarrisonCooldownHours = garrisonCooldown.Value;
            Takeover.PatrolAtCamp = patrolAtCamp.Value;
        }

        void ApplyFactionSettings()
        {
            Factions.HomeRadius = homeRadius.Value;
            var pw = new List<string>();
            foreach (string w in (peopleNames.Value ?? "").Split(',')) { string n = Util.Norm(w); if (n.Length > 0) pw.Add(n); }
            Factions.PeopleWords = pw.ToArray();
            var hf = new List<string>();
            foreach (string w in (humanFactions.Value ?? "").Split(',')) { string n = Util.Norm(w); if (n.Length > 0) hf.Add(n); }
            Factions.HumanFactions = hf.ToArray();
        }

        void ParseGenTypes()
        {
            for (int i = 0; i < allowedGen.Length; i++) allowedGen[i] = false;
            allowTakeover = false;
            allowDeliver = false;
            string[] parts = (genTypes.Value ?? "").Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "fetch": allowedGen[0] = true; break;
                    case "kill": allowedGen[1] = true; break;
                    case "goto": allowedGen[2] = true; break;
                    case "talk": allowedGen[3] = true; break;
                    case "clear": allowedGen[4] = true; break;
                    case "takeover": allowTakeover = true; break;
                    case "deliver": allowDeliver = true; break;
                }
            }
            anyGen = false;
            for (int i = 0; i < allowedGen.Length; i++) if (allowedGen[i]) anyGen = true;
            if (allowTakeover || allowDeliver) anyGen = true;
        }

        static Type FindType(string fullName)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }



        // ------------------------------------------------------------------
        // Per-frame work
        // ------------------------------------------------------------------
        internal void LateFrame()
        {
            try { Hire.LateUpdate(); } catch (Exception e) { LogOnce("HireLateUpdate", e); }
        }

        internal void Frame()
        {
            try { Game.WatchSpawns(); } catch (Exception) { }
            try
            {
                while (deferred.Count > 0)
                {
                    Action a = deferred.Dequeue();
                    try { a(); } catch (Exception e) { LogOnce("action", e); }
                }

                try { Hire.EarlyTeach(); } catch (Exception e) { LogOnce("HireTeach", e); }   // before any Apocapatrol crew spawns

                if (!ready) return;
                float dt = Time.deltaTime;                    // stops while paused
                double gh = Game.GameHours();                 // delivery time limits run on the in-game clock (sleeping counts)
                if (gh >= 0.0 && lastGameHours >= 0.0) dt = (float)Math.Max(0.0, (gh - lastGameHours) * 3600.0);
                lastGameHours = gh;
                for (int i = 0; i < book.Active.Count; i++)
                    if (book.Active[i].Type == QType.Deliver) book.Active[i].TimeLeft -= dt;
                try { Takeover.Update(); } catch (Exception e) { LogOnce("TakeoverUpdate", e); }
                try { Hire.Update(); } catch (Exception e) { LogOnce("HireUpdate", e); }
                try { SyncTalkUi(); } catch (Exception e) { LogOnce("SyncTalkUi", e); }

                if (showLog && GamePaused()) showLog = false;
            }
            catch (Exception e) { LogOnce("Update", e); }
        }

        void Defer(Action a) { deferred.Enqueue(a); }

        // Fail-safe for takeover quests: the camp's people only exist while you are near, so a camp can turn out to
        // have fewer people than the quest asked for, or to be cleared already. If you stand at the camp for a while
        // (long enough for its spawners to have fired) and nobody of its faction is alive, the camp counts as cleared.
        readonly Dictionary<string, float> campEmptyFor = new Dictionary<string, float>();
        const float EmptyCampSeconds = 15f;

        void CheckEmptyCamp(Quest q, Vector3 playerWorld)
        {
            Vector3 camp = Game.Abs(q.Pos);
            float near = Math.Max(30f, q.Radius * 0.6f);
            if (Game.HDist(playerWorld, camp) > near || Takeover.CountDefenders(camp, q.Target, q.Radius) > 0)
            {
                campEmptyFor.Remove(q.Id);
                return;
            }
            float since;
            if (!campEmptyFor.TryGetValue(q.Id, out since)) { campEmptyFor[q.Id] = Time.unscaledTime; return; }
            if (Time.unscaledTime - since < EmptyCampSeconds) return;
            campEmptyFor.Remove(q.Id);
            q.Progress = q.Count;
            q.ObjectiveDone = true;
            nextRefresh = 0f;
            Log.LogInfo(q.Title + ": camp is empty, counting it as cleared.");
            Quest tq = q;
            Defer(delegate
            {
                int sent = Takeover.Begin(tq, rng);
                offers.Clear();             // offers waiting on NPCs may point at the camp that just changed hands
                Faction win = Factions.ByKey(tq.Home);
                AddToast(sent > 0 && win != null ? "The " + tq.Label + " is yours. " + sent + " of " + win.Name + "'s people are on their way." : "The " + tq.Label + " is yours.");
            });
            if (!q.ReturnToGiver) { if (Pay(q)) Complete(q); }
            else AddToast("Objective complete: " + q.Title + " (return to the " + q.GiverName + ")");
        }

        // Position-based objectives and the tracker text
        internal void Tick()
        {
            Transform pl = Game.Player();
            if (pl == null) return;
            Vector3 pp = pl.position;






            Quest[] snapshot = book.Active.ToArray();
            double nowH = Game.GameHours();
            for (int i = 0; i < snapshot.Length; i++)
            {
                Quest q = snapshot[i];
                if (q.Type == QType.Deliver) { try { DeliveryRecipientCheck(q, pp); } catch (Exception e) { LogOnce("DeliveryRecipient", e); } }
                if (q.Type == QType.Deliver && q.TimeLeft <= 0f) { if (Returning(q)) { CancelQuest(q, "ran out of time returning the goods"); continue; } if (q.NoRecipient) CancelStranded(q); else DeliveryStolen(q); continue; }
                if (q.Type != QType.Deliver && q.Deadline > 0.0 && nowH >= 0.0 && nowH >= q.Deadline) { FailQuest(q, "ran out of time"); continue; }
                if (q.Type == QType.Takeover && !q.ObjectiveDone && q.HasPos) { ScoutCamp(q, pp); CheckEmptyCamp(q, pp); }
                if (q.Type == QType.Goto && !q.ObjectiveDone && q.HasPos && Game.HDist(pp, Game.Abs(q.Pos)) <= q.Radius)
                {
                    q.ObjectiveDone = true;
                    if (!q.ReturnToGiver) { if (Pay(q)) Complete(q); }
                    else AddToast("Objective reached: " + q.Title + " (return to the " + q.GiverName + ")");
                }
            }

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 1f;
                RefreshLines(pl);
            }
        }

        // ---- Immersive Map: a ! at each quest target, a check mark on a giver who is waiting for the hand-in ----
        float nextMapSync;
        readonly Dictionary<string, int> haveCache = new Dictionary<string, int>();     // fetch quests: how many the player has right now (set once a second by RefreshLines)
        internal void SyncMapMarkers()
        {
            if (!ready || Time.unscaledTime < nextMapSync) return;
            nextMapSync = Time.unscaledTime + 1f;
            if (!MapLink.Available) return;
            var want = new Dictionary<string, Vector3>();      // key -> world position
            for (int i = 0; i < book.Active.Count; i++)
            {
                Quest q = book.Active[i];
                int heldNow;
                bool ready4Hand = q.ObjectiveDone || (q.Type == QType.Fetch && haveCache.TryGetValue(q.Id, out heldNow) && heldNow >= q.Count);
                if (!ready4Hand)
                {
                    if (q.HasPos && q.Type != QType.Fetch) want["Q:" + q.Id] = Game.Abs(q.Pos);
                }
                else if (q.ReturnToGiver && q.Type != QType.Talk)
                {
                    Vector3 gw;
                    bool have = false;
                    gw = Vector3.zero;
                    Transform live = Game.FindLive(q.GiverId);
                    if (live != null) { gw = live.position; have = true; }
                    else if (q.HasGiverPos) { gw = Game.Abs(q.GiverPos); have = true; }
                    if (have) want["T:" + q.Id] = gw;
                }
            }
            MapLink.Sync(want);
        }

        void RefreshLines(Transform pl)
        {
            trackerLines.Clear();
            logLines.Clear();
            Vector3 pp = pl != null ? pl.position : Vector3.zero;
            for (int i = 0; i < book.Active.Count; i++)
            {
                Quest q = book.Active[i];
                int have = 0;
                if (q.Type == QType.Fetch && pl != null)
                    have = Game.CountStacks(Game.FindItems(q.Target, pp, handRadius.Value, countCarried.Value));
                if (q.Type == QType.Fetch) haveCache[q.Id] = have;
                string tl = DeadlineText(q);
                try
                {
                    trackerLines.Add(q.Title + " - " + ShortStatus(q, have, pl) + (tl.Length > 0 && q.Type != QType.Deliver ? ", " + tl + " left" : ""));
                    logLines.Add("<b>" + q.Title + "</b>   reward " + q.Reward + " nuts" + (q.Chain.Length > 0 ? "   [chain: " + q.Chain + "]" : "") + "\n    " + Objective(q, have, pl) + DirectionsLine(q, have, pl)
                                 + (tl.Length > 0 ? "\n    Time left: " + tl : "") + "\n    Given by: " + q.GiverName);
                }
                catch (Exception e)
                {
                    LogOnce("RefreshLines:" + q.Id, e);
                    trackerLines.Add(q.Title);
                    logLines.Add("<b>" + q.Title + "</b>   reward " + q.Reward + " nuts\n    Given by: " + q.GiverName);
                }
            }
            var sb = new StringBuilder();
            string repLine = ReputationText();
            if (repLine.Length > 0) sb.Append(repLine).Append("\n\n");
            if (logLines.Count == 0) sb.Append("No active quests. Talk to someone and ask for work.");
            for (int i = 0; i < logLines.Count; i++) { sb.Append(logLines[i]); sb.Append("\n\n"); }
            logText = sb.ToString();
            SnapshotRows();
        }

        // how long a job has left, in in-game time ("1d 6h", "7h", "<1h"); empty when it has no limit
        string DeadlineText(Quest q)
        {
            if (q.Type == QType.Deliver || q.Deadline <= 0.0) return "";
            double now = Game.GameHours();
            if (now < 0.0) return "";
            double left = Math.Max(0.0, q.Deadline - now);
            if (left >= 24.0)
            {
                int d = (int)(left / 24.0);
                int h = (int)Math.Round(left - d * 24.0);
                if (h >= 24) { d++; h = 0; }
                return d + "d" + (h > 0 ? " " + h + "h" : "");
            }
            return left < 1.0 ? "<1h" : (int)Math.Ceiling(left) + "h";
        }

        static bool CountUnknown(Quest q) { return q.Type == QType.Takeover && q.Count <= 0; }

        string ShortStatus(Quest q, int have, Transform pl)
        {
            if (q.ObjectiveDone && q.Type != QType.Fetch)
                return q.Type == QType.Kill ? "done, report to the " + q.GiverName + (bodyBonus.Value > 0f ? " (bodies pay extra)" : "") : "done, return to the " + q.GiverName;
            switch (q.Type)
            {
                case QType.Fetch: return have + "/" + q.Count + " " + q.Label;
                case QType.Kill: return q.Progress + "/" + q.Count + " " + q.Label;
                case QType.Clear: return q.Progress + "/" + q.Count + " cleared";
                case QType.Takeover:
                    if (CountUnknown(q))
                    {
                        string far = "";
                        if (pl != null && q.HasPos) { Vector3 ct = Game.Abs(q.Pos); far = ", " + (int)Game.HDist(pl.position, ct) + " m " + Util.Compass(ct.x - pl.position.x, ct.z - pl.position.z); }
                        return "head count unknown" + far;
                    }
                    return q.Progress + "/" + q.Count + " of the " + q.Label;
                case QType.Deliver:
                    {
                        string where = "";
                        if (pl != null && q.HasPos) { Vector3 dt = Game.Abs(q.Pos); where = (int)Game.HDist(pl.position, dt) + " m " + Util.Compass(dt.x - pl.position.x, dt.z - pl.position.z) + ", "; }
                        return where + Clock(q.TimeLeft) + " left";
                    }
                case QType.Goto:
                    if (pl == null || !q.HasPos) return "go to the spot";
                    Vector3 t = Game.Abs(q.Pos);
                    return (int)Game.HDist(pl.position, t) + " m " + Util.Compass(t.x - pl.position.x, t.z - pl.position.z);
                default:
                    if (pl == null) return "find someone far away";
                    return (int)Game.HDist(pl.position, Game.Abs(q.Start)) + "/" + (int)q.MinDistance + " m away from " + q.GiverName;
            }
        }

        string Objective(Quest q, int have, Transform pl)
        {
            if (Returning(q)) return "There was nobody to deliver to. Return all " + q.Count + " " + q.Label + " to the " + q.GiverName + ".";
            if (q.ObjectiveDone && q.Type == QType.Kill) return "Kills done. Report to the " + q.GiverName + (bodyBonus.Value > 0f ? " and bring the bodies for a bonus." : ".");
            if (q.ObjectiveDone && q.Type != QType.Fetch) return "Done. Return to the " + q.GiverName + ".";
            switch (q.Type)
            {
                case QType.Fetch: return "Bring " + q.Count + " " + q.Label + " to the " + q.GiverName + " (you have " + have + ").";
                case QType.Kill:
                    return "Kill " + q.Count + " " + q.Label + (q.HasPos ? " within " + (int)q.Radius + " m of the " + q.GiverName : "") + " (" + q.Progress + "/" + q.Count + "), then report back to the " + q.GiverName + ".";
                case QType.Clear: return "Kill " + q.Count + " hostiles within " + (int)q.Radius + " m of where you took the job (" + q.Progress + "/" + q.Count + ").";
                case QType.Takeover:
                    {
                        Faction win = Factions.ByKey(q.Home);
                        string then = win != null ? " Then " + win.Name + " moves in." : "";
                        if (CountUnknown(q))
                            return "Clear the " + q.Label + ": kill everyone there within " + (int)q.Radius + " m of it. How many people that is will show here once you are within " + (int)scoutRange.Value + " m (" + ShortStatus(q, have, pl) + ")." + then;
                        return "Clear the " + q.Label + ": kill " + q.Count + " of its people within " + (int)q.Radius + " m of it (" + q.Progress + "/" + q.Count + ")." + then;
                    }
                case QType.Goto:
                    if (q.Checkup) return "Look in on the " + (string.IsNullOrEmpty(q.Label) ? "camp" : q.Label) + ": " + ShortStatus(q, have, pl) + " away. Then report back to the " + q.GiverName + ".";
                    return "Reach the " + (string.IsNullOrEmpty(q.Label) ? "spot" : q.Label) + ": " + ShortStatus(q, have, pl) + " away." + (q.ReturnToGiver ? " Then report back to the " + q.GiverName + "." : "");
                case QType.Deliver:
                    return "Take " + q.Count + " " + q.Label + " to the " + q.Place + " and talk to someone there (" + ShortStatus(q, have, pl) + "). If time runs out, the " + q.GiverName + "'s people will call it theft.";
                default: return "Find someone at least " + (int)q.MinDistance + " m from where you took the job and talk to them.";
            }
        }

        // ---- directions: where to go next for a job (shown in the quest log and available as {where} in quests.txt) ----
        internal class AimInfo
        {
            public float At;
            public string Key = "";
            public bool Has;
            public Vector3 Rel;          // origin-relative, because the game shifts the world as you travel
            public string What = "";
        }
        readonly Dictionary<string, AimInfo> aimCache = new Dictionary<string, AimInfo>();

        static bool HandIn(Quest q, int have) { return q.Type == QType.Fetch ? have >= q.Count : q.ObjectiveDone; }

        // the next place to go for this job, worked out at most every 4 seconds (the scans walk the loaded world)
        AimInfo Aim(Quest q, int have, Transform pl)
        {
            if (_aimFrame != Time.frameCount) { _aimFrame = Time.frameCount; _aimComputed = 0; }
            string key = HandIn(q, have) ? "hand" : "work";
            AimInfo a;
            if (aimCache.TryGetValue(q.Id, out a) && a.Key == key && Time.unscaledTime - a.At < 4f && Time.unscaledTime >= a.At) return a;
            if (a != null && a.Key == key && _aimComputed > 0) return a;
            _aimComputed++;
            a = new AimInfo();
            a.At = Time.unscaledTime;
            a.Key = key;
            aimCache[q.Id] = a;
            try { ComputeAim(q, have, pl, a); }
            catch (Exception e) { LogOnce("Aim:" + q.Id, e); a.Has = false; }
            return a;
        }

        static void AimAt(AimInfo a, Vector3 world, string what)
        {
            a.Has = true;
            a.Rel = Game.Rel(world);
            a.What = what;
        }

        void ComputeAim(Quest q, int have, Transform pl, AimInfo a)
        {
            Vector3 pp = pl.position;
            if (HandIn(q, have))
            {
                if (q.Type == QType.Goto && !q.ReturnToGiver) return;
                string giver = "the " + q.GiverName;
                Transform live = Game.FindLive(q.GiverId);
                if (live != null) AimAt(a, live.position, giver);
                else if (q.HasGiverPos) AimAt(a, Game.Abs(q.GiverPos), giver + " (last seen)");
                return;
            }
            switch (q.Type)
            {
                case QType.Fetch:
                    {
                        float rad = itemRange.Value > 0f ? itemRange.Value : 4000f;
                        List<ItemHit> hits = Game.FindItems(q.Target, pp, rad, false);
                        ItemHit best = null;
                        for (int i = 0; i < hits.Count; i++)
                        {
                            if (hits[i].Go == null || hits[i].Reserve || hits[i].Dist <= handRadius.Value) continue;      // items within hand range are already counted
                            if (best == null || hits[i].Dist < best.Dist) best = hits[i];
                        }
                        if (best != null) AimAt(a, best.Go.transform.position, "the nearest " + (q.Label.Length > 0 ? q.Label : "one"));
                        return;
                    }
                case QType.Kill:
                    {
                        Faction home = Factions.ByKey(q.Home);
                        List<EnemyInfo> foes = Game.FindEnemies(pp, 4000f);
                        EnemyInfo best = null;
                        float bd = 0f;
                        Vector3 centre = q.HasPos ? Game.Abs(q.Pos) : pp;
                        for (int i = 0; i < foes.Count; i++)
                        {
                            EnemyInfo e = foes[i];
                            if (!KillMatches(q.Target, e.Name, e.Name)) continue;
                            if (Factions.Protected(home, e.Tag, e.Name, e.Pos, e.Go)) continue;
                            if (q.HasPos && Game.HDist(e.Pos, centre) > q.Radius) continue;
                            float d = Game.HDist(e.Pos, pp);
                            if (best == null || d < bd) { best = e; bd = d; }
                        }
                        if (best != null) AimAt(a, best.Pos, "the nearest " + (q.Label.Length > 0 ? q.Label : best.Name) + " you can see");
                        else if (q.HasPos) AimAt(a, centre, "the hunting ground (centre of the " + (int)q.Radius + " m area)");
                        return;
                    }
                case QType.Clear:
                    AimAt(a, Game.Abs(q.HasPos ? q.Pos : q.Start), "the patrol area (centre)");
                    return;
                case QType.Takeover:
                    if (q.HasPos) AimAt(a, Game.Abs(q.Pos), "the " + (q.Label.Length > 0 ? q.Label : "camp"));
                    return;
                case QType.Goto:
                    if (q.HasPos) AimAt(a, Game.Abs(q.Pos), "the " + (string.IsNullOrEmpty(q.Label) ? "spot" : q.Label));
                    return;
                case QType.Deliver:
                    if (q.HasPos) AimAt(a, Game.Abs(q.Pos), "the " + (string.IsNullOrEmpty(q.Place) ? "destination" : q.Place));
                    return;
                case QType.Talk:
                    {
                        // anyone friendly far enough from where you took the job: the nearest camp that qualifies
                        Faction home = Factions.ByKey(q.Home);
                        Vector3 start = Game.Abs(q.Start);
                        List<Poi> camps = Game.FindPois(pp, 0f, 4000f, "Camp");
                        Poi best = null;
                        float bd = 0f;
                        for (int i = 0; i < camps.Count; i++)
                        {
                            Poi c = camps[i];
                            if (Game.HDist(c.Pos, start) < q.MinDistance) continue;
                            if (c.Faction.Length > 0 && !Factions.Friendly(home, Factions.ByKey(c.Faction))) continue;
                            float d = Game.HDist(c.Pos, pp);
                            if (best == null || d < bd) { best = c; bd = d; }
                        }
                        if (best != null) AimAt(a, best.Pos, "a camp far enough away");
                        return;
                    }
            }
        }

        static string Far(int m) { return m >= 1000 ? (m / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km" : m + " m"; }

        static string Place(AimInfo a, Transform pl)
        {
            Vector3 t = Game.Abs(a.Rel);
            int d = (int)Game.HDist(pl.position, t);
            return d < 30 ? "right here" : Far(d) + " " + Util.Compass(t.x - pl.position.x, t.z - pl.position.z) + " of you";
        }

        static string Sentence(AimInfo a, Transform pl)
        {
            string w = a.What;
            if (w.Length > 0) w = char.ToUpperInvariant(w[0]) + w.Substring(1);
            return w + " is " + Place(a, pl) + ".";
        }

        string DirectionsLine(Quest q, int have, Transform pl)
        {
            if (pl == null) return "";
            AimInfo a = Aim(q, have, pl);
            if (a.Has) return "\n    Directions: " + Sentence(a, pl);
            switch (q.Type)
            {
                case QType.Fetch: return "\n    Directions: none in sight within " + Far((int)(itemRange.Value > 0f ? itemRange.Value : 4000f)) + ". Search camps, wrecks and cars, or try a merchant.";
                case QType.Kill: return "\n    Directions: none in sight. Look around the area.";
                case QType.Talk: return "\n    Directions: no camp in range yet. Head " + Far((int)q.MinDistance) + " or more from where you took the job.";
                default: return "";
            }
        }

        // A takeover's head count is not known until the camp is in view. Once you are close enough for its people to have
        // loaded, count them (and keep counting while you have not killed anyone, since they appear as you come nearer).
        readonly HashSet<string> scouting = new HashSet<string>();
        void ScoutCamp(Quest q, Vector3 playerWorld)
        {
            if (q.Count <= 0) scouting.Add(q.Id);
            if (!scouting.Contains(q.Id) || q.Progress > 0 && q.Count > 0) return;
            Vector3 camp = Game.Abs(q.Pos);
            if (Game.HDist(playerWorld, camp) > scoutRange.Value) return;
            int alive = Takeover.CountDefenders(camp, q.Target, q.Radius);
            if (alive <= 0) return;
            int need = q.Progress + alive;
            if (need == q.Count) return;
            if (q.Count > 0 && need < q.Count) return;            // only ever more: people that load in late are added
            bool first = q.Count <= 0;
            q.Count = need;
            nextRefresh = 0f;
            if (first) AddToast("The " + q.Label + " has " + need + " people. Quest log updated: " + q.Title + " 0/" + need);
            else Log.LogInfo(q.Title + ": more people came into view, now " + need + ".");
        }

        // ------------------------------------------------------------------
        // Rewards
        // ------------------------------------------------------------------
        bool Pay(Quest q)
        {
            if (!Game.AddPartyMoney(q.Reward, moneyVar.Value))
            {
                AddToast("Could not pay the reward: money variable not found (see the BepInEx log).");
                return false;
            }
            return true;
        }

        void Complete(Quest q)
        {
            book.Active.Remove(q);
            if (q.Once && !q.Id.StartsWith("gen:", StringComparison.Ordinal)) book.DoneIds.Add(q.Id);
            book.TotalDone++;
            AddToast("Quest complete: " + q.Title + "   +" + q.Reward + " nuts");
            if (!string.IsNullOrEmpty(q.Home)) ChangeRep(q.Home, Math.Max(1, q.RepGain), null);
            SetQuestFlags(q, "done");
            nextRefresh = 0f;
        }

        // A once-only quest (or chain step) that ended without being completed can be picked up again: it was never marked
        // done, and the things that would hold it back (a neighbour's recent offer, the chain's 24 hour rule, the giver's
        // cooldown) are cleared.
        void Reopen(Quest q, bool clearCooldown = true)
        {
            if (q == null || !q.Once || string.IsNullOrEmpty(q.Id) || q.Id.StartsWith("gen:", StringComparison.Ordinal)) return;
            book.DoneIds.Remove(q.Id);
            chainOffers.Remove(q.Id);
            string idKey = "id:" + q.Id, qk = QuestGen.KeyOf(q);
            ledger.RemoveAll(delegate (Offered o) { return o.Key == idKey || (qk.Length > 0 && o.Key == qk); });
            string giverKey = !string.IsNullOrEmpty(q.GiverId) ? q.GiverId : q.GiverName;
            if (clearCooldown && !string.IsNullOrEmpty(giverKey)) book.GiverStamp.Remove(giverKey);
        }

        // The job ran out of time (or similar): it is removed and the giver's faction thinks a little less of you.
        // Reputation lost when a job fails or is given up: FailPenaltyMultiplier x what finishing it would have given.
        int FailPenalty(Quest q)
        {
            if (failPenaltyMult.Value <= 0f) return 0;
            return Math.Max(1, (int)Math.Round(Math.Max(1, q.RepGain) * failPenaltyMult.Value));
        }

        void FailQuest(Quest q, string why)
        {
            if (!book.Active.Remove(q)) return;
            Reopen(q);
            nextRefresh = 0f;
            AddToast("Job failed (" + why + "): " + q.Title);
            if (!string.IsNullOrEmpty(q.Home) && FailPenalty(q) > 0) ChangeRep(q.Home, -FailPenalty(q), null);
            SetQuestFlags(q, "failed");
        }

        // The job is off through nobody's fault (the giver died): no reputation is lost.
        void CancelQuest(Quest q, string why)
        {
            if (Returning(q)) { FailQuest(q, why); return; }
            if (!book.Active.Remove(q)) return;
            Reopen(q);
            nextRefresh = 0f;
            AddToast("Job cancelled (" + why + "): " + q.Title);
            SetQuestFlags(q, "failed");
        }

        // Flags in Apocalypter Talk's story file, so dialogue files can react: quest_<id>_active / _done / _failed.
        static string FlagBase(Quest q)
        {
            if (q == null || q.Id.StartsWith("gen:", StringComparison.Ordinal)) return "";
            var sb = new StringBuilder("quest_");
            foreach (char c in q.Id.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }

        void SetQuestFlags(Quest q, string state)
        {
            string fb = FlagBase(q);
            if (fb.Length == 0 || !TalkApiLink.Available) return;
            TalkApiLink.SetFlag(fb + "_active", state == "active");
            if (state == "done") TalkApiLink.SetFlag(fb + "_done", true);
            if (state == "failed") TalkApiLink.SetFlag(fb + "_failed", true);
            if (state == "active") TalkApiLink.SetFlag(fb + "_failed", false);
        }

        // what a job pays, relative to the normal reward, for the faction's reputation with you
        float PayMultiplier(string homeKey)
        {
            if (string.IsNullOrEmpty(homeKey) || payPerRep.Value == 0f) return 1f;
            float m = 1f + payPerRep.Value * RepOf(homeKey);
            m = Mathf.Max(m, Mathf.Max(0f, minPay.Value));
            if (maxPay.Value > 0f) m = Mathf.Min(m, maxPay.Value);
            return m;
        }

        // ------------------------------------------------------------------
        // Deliveries and reputation
        // ------------------------------------------------------------------
        double lastGameHours = -1.0;

        static string Clock(float seconds)
        {
            int m = Mathf.Max(0, Mathf.CeilToInt(seconds / 60f));      // in-game minutes
            return m >= 60 ? (m / 60) + "h " + (m % 60).ToString("00") + "m" : m + "m";
        }

        // Hands the goods over when you talk to someone friendly at the destination camp. True when it handled the talk.
        bool TryDeliver(Quest[] active, string npc, Transform pl)
        {
            Transform t = bridge.Npc;
            if (t == null) return false;
            for (int i = 0; i < active.Length; i++)
            {
                Quest q = active[i];
                if (q.Type != QType.Deliver || !q.HasPos) continue;
                if (Game.HDist(t.position, Game.Abs(q.Pos)) > q.Radius) continue;
                if (Game.IsPlayerEnemy(t.gameObject)) continue;                 // anyone at the camp who isn't hostile to you
                List<ItemHit> hits = Game.FindItems(q.Target, pl.position, handRadius.Value, countCarried.Value);
                int have = Game.CountStacks(hits);
                if (have < q.Count) { bridge.Say(Format(q.Remind, q, have)); Speak(q, "remind"); return false; }
                if (Game.MoneyVar(moneyVar.Value) == null) { AddToast("Could not pay the reward: money variable not found (see the BepInEx log)."); return true; }
                Game.TakeItems(hits, q.Count, t.position);
                bridge.Say(Format(q.Done, q, q.Count)); Speak(q, "done");
                if (Pay(q)) Complete(q);
                return true;
            }
            return false;
        }

        // At the camp but nobody there will take the goods (empty camp, everyone dead, or it belongs to someone else): say so, and
        // let the job end without the theft penalty if the clock runs out. The giver takes the goods back ("Give back the goods").
        void DeliveryRecipientCheck(Quest q, Vector3 pp)
        {
            bool returning = Returning(q);
            if (returning ? !q.HasGiverPos : !q.HasPos) return;
            if (returning && DeliveryRecipient(q, Vector3.zero, float.PositiveInfinity, true)) { q.NoRecipientSince = -1; return; }
            var camps = Services.Camps; if (camps == null) return;
            Vector3 relative = returning ? q.GiverPos : q.Pos;
            var state = camps.At(relative, 120f);
            if (state == CampState.Cleared) { FailQuest(q, returning ? "return camp is dead" : "destination camp is dead"); return; }
            Vector3 camp = Game.Abs(relative); float radius = returning ? 120f : q.Radius;
            float arrival = Mathf.Max(5f, Mathf.Min(30f, radius * .6f));
            if (Game.HDist(pp, camp) > arrival || state != CampState.Ready || DeliveryRecipient(q, camp, radius, returning))
            { q.NoRecipientSince = -1; _emptyScans.Remove(q.Id); return; }
            int scan;
            if (q.NoRecipientSince < 0) { q.NoRecipientSince = Time.time; _emptyScans[q.Id] = QuestWorld.Revision; return; }
            if (Time.time - q.NoRecipientSince < 15f || !_emptyScans.TryGetValue(q.Id, out scan) || scan == QuestWorld.Revision) return;
            _emptyScans.Remove(q.Id); q.NoRecipientSince = -1;
            if (returning || !q.HasGiverPos) { FailQuest(q, returning ? "nobody is there to take the goods back" : "return location is unknown"); return; }
            q.NoRecipient = true; q.Place = "WW:return-empty-delivery"; q.HasPos = false; q.ReturnToGiver = q.ObjectiveDone = true; q.Reward = 0;
            q.Title = "Return " + q.Count + " " + q.Label; aimCache.Remove(q.Id); nextRefresh = nextMapSync = 0; uiDirty = true;
            AddToast("There was nobody to deliver to. Return the " + q.Label + " to the " + q.GiverName + ".");
        }

        // The clock ran out on a delivery that could not be made: no penalty, the giver may offer again.
        void CancelStranded(Quest q)
        {
            if (!book.Active.Remove(q)) return;
            Reopen(q);
            nextRefresh = 0f;
            AddToast("The " + q.Title + " fell through: nobody was there to take it. Return the goods to the " + q.GiverName + " if you want.");
        }

        // Back at the giver: hand the goods back and call it off without a penalty.
        void ReturnGoods(Quest q)
        {
            if (!book.Active.Contains(q)) return;
            bridge.You((Returning(q) ? "There was nobody to deliver to. Here are your " : "I can't make that delivery. Here are your ") + q.Label + ".");
            Transform pl = Game.Player();
            List<ItemHit> hits = Game.FindItems(q.Target, pl != null ? pl.position : Vector3.zero, handRadius.Value, countCarried.Value);
            int have = Game.CountStacks(hits);
            if (have < q.Count)
            {
                bridge.Say("That's " + have + ". I gave you " + q.Count + ". Bring all of them back, or get them where they're going.");
                return;
            }
            Game.TakeItems(hits, q.Count, bridge.Npc != null ? (Vector3?)bridge.Npc.position : null);
            book.Active.Remove(q);
            Reopen(q);
            bridge.Say("Fine. I'll find someone else.");
            nextRefresh = 0f;
        }

        // Called off at the giver without the goods: same as running out of time.
        void AbandonDelivery(Quest q)
        {
            if (Returning(q)) { CancelQuest(q, "called off"); return; }
            if (!book.Active.Contains(q)) return;
            bridge.You("I can't make that delivery.");
            bridge.Say("And my " + q.Label + "? Don't show your face here again for a while.");
            DeliveryStolen(q);
        }

        void DeliveryStolen(Quest q)
        {
            if (Returning(q)) { CancelQuest(q, "ran out of time returning the goods"); return; }
            if (!book.Active.Remove(q)) return;
            Reopen(q, false);                       // "don't show your face here for a while": the giver's cooldown stays
            nextRefresh = 0f;
            Faction f = Factions.ByKey(q.Home);
            string who = f != null ? f.Name : "The " + q.GiverName + "'s people";
            AddToast((q.TimeLeft <= 0f ? "Out of time: you kept the " : "You kept the ") + q.Label + ". " + who + " will remember that.");
            if (!string.IsNullOrEmpty(q.Home) && repFail.Value != 0) ChangeRep(q.Home, -Math.Abs(repFail.Value), who);
        }

        int RepOf(string key)
        {
            int r;
            return !string.IsNullOrEmpty(key) && book.Rep.TryGetValue(Util.Norm(key), out r) ? r : 0;
        }

        void ChangeRep(string key, int delta, string who)
        {
            string k = Util.Norm(key);
            if (k.Length == 0 || delta == 0) return;
            int before = RepOf(k);
            Faction f = Factions.ByKey(k);
            string name = who ?? (f != null ? f.Name : key);
            if (delta > 0 && before >= RepMax) { AddToast(name + " reputation is capped at " + RepMax + ": no more to gain."); return; }
            if (delta < 0 && before <= -RepMax) return;
            int now = Mathf.Clamp(before + delta, -RepMax, RepMax);
            int applied = now - before;
            book.Rep[k] = now;
            double gh = Game.GameHours();
            if (gh >= 0.0) book.RepStamp[k] = gh; else book.RepStamp.Remove(k);
            AddToast(name + " reputation " + (applied > 0 ? "+" : "") + applied + " (now " + now + (now == RepMax ? ", the maximum" : now == -RepMax ? ", the lowest" : "") + ")");
            if (repHostile.Value < 0 && now <= repHostile.Value) AddToast(name + " now treat you as an enemy.");
        }

        string ReputationText()
        {
            if (book.Rep.Count == 0) return "";
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> r in book.Rep)
            {
                if (r.Value == 0) continue;
                Faction f = Factions.ByKey(r.Key);
                int pay = (int)Math.Round((PayMultiplier(r.Key) - 1f) * 100f);
                parts.Add((f != null ? f.Name : r.Key) + " " + (r.Value > 0 ? "+" : "") + r.Value + (pay != 0 ? " (pay " + (pay > 0 ? "+" : "") + pay + "%)" : ""));
            }
            return parts.Count == 0 ? "" : "Reputation (-" + RepMax + " to +" + RepMax + "): " + string.Join(", ", parts.ToArray());
        }

        // Slow recovery of bad reputation, and the game's hostility switch for factions you have angered enough.
        float nextRepTick;
        readonly HashSet<int> madeHostile = new HashSet<int>();
        internal void ReputationTick()
        {
            if (Time.unscaledTime < nextRepTick) return;
            nextRepTick = Time.unscaledTime + 3f;
            double now = Game.GameHours();
            if (now >= 0.0 && repRecoverHours.Value > 0f)
            {
                foreach (string k in new List<string>(book.Rep.Keys))
                {
                    if (book.Rep[k] >= 0) continue;
                    double since;
                    if (!book.RepStamp.TryGetValue(k, out since) || since > now) { book.RepStamp[k] = now; continue; }
                    if (now - since < repRecoverHours.Value) continue;
                    book.Rep[k] = book.Rep[k] + 1;
                    book.RepStamp[k] = now;
                }
            }
            bool anyAngry = false;
            if (repHostile.Value < 0) foreach (int v in book.Rep.Values) if (v <= repHostile.Value) { anyAngry = true; break; }
            if (!anyAngry && madeHostile.Count == 0) return;
            List<EnemyInfo> folk = Game.FindPeople(Factions.PeopleWords);
            var alive = new HashSet<int>();
            for (int i = 0; i < folk.Count; i++)
            {
                GameObject go = folk[i].Go;
                if (go == null) continue;
                alive.Add(go.GetInstanceID());
                string ftag = Util.Norm(folk[i].Tag);
                Faction f = Array.IndexOf(Factions.HumanFactions, ftag) >= 0 ? Factions.ByKey(ftag) : null;   // by tag only: cheap
                bool angry = f != null && repHostile.Value < 0 && RepOf(f.Key) <= repHostile.Value;
                if (angry) { if (Game.SetPlayerEnemy(go, true)) madeHostile.Add(go.GetInstanceID()); }
                else if (madeHostile.Remove(go.GetInstanceID())) Game.SetPlayerEnemy(go, false);   // forgiven: undo only what we did
            }
            madeHostile.IntersectWith(alive);                                  // forget people who are gone
        }

        void AddToast(string text)
        {
            if (Services.QuestUi != null && Services.QuestUi.Notify(text)) { Core.Log.Always("[Quests] " + text); return; }
            var t = new Toast();
            t.Text = text;
            t.Until = Time.unscaledTime + 6f;
            toasts.Add(t);
            if (toasts.Count > 4) toasts.RemoveAt(0);
            Log.LogInfo(text);
        }

        // ------------------------------------------------------------------
        // Kills (called from the Harmony hook)
        // ------------------------------------------------------------------
        static bool KillMatches(string target, string ownerName, string deadName)
        {
            if (string.IsNullOrEmpty(target) || target == "any" || target == "*") return true;
            string owner = Util.Norm(ownerName);
            string dead = Util.Norm(deadName);
            string[] parts = target.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string t = Util.Norm(parts[i]);
                if (t.Length == 0) continue;
                if (t == "any" || t == "*" || owner.Contains(t) || dead.Contains(t)) return true;
            }
            return false;
        }

        internal void OnEnemyKilled(string ownerName, string deadName, Vector3 worldPos, string ownerTag, GameObject ownerGo)
        {
            if (!ready || book.Active.Count == 0) return;
            // a quest giver died: their jobs are off, and that costs you no reputation
            if (ownerGo != null)
            {
                string deadObj = ownerGo.name;
                Quest[] mineNow = book.Active.ToArray();
                for (int gi = 0; gi < mineNow.Length; gi++)
                    if (!string.IsNullOrEmpty(mineNow[gi].GiverId) && mineNow[gi].GiverId == deadObj) CancelQuest(mineNow[gi], mineNow[gi].GiverName + " died");
                if (book.Active.Count == 0) return;
            }
            Transform pl = Game.Player();
            if (Core.Multiplayer.Hosting)
            { if (Core.Multiplayer.PlayerDistance(worldPos, pl, true) > killRadius.Value) return; }
            else
            { if (pl == null || Game.HDist(pl.position, worldPos) > killRadius.Value) return; }
            Vector3 rel = Game.Rel(worldPos);

            Quest[] snapshot = book.Active.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                Quest q = snapshot[i];
                if (q.ObjectiveDone) continue;
                bool hit = false;
                if ((q.Type == QType.Kill || q.Type == QType.Clear) && Factions.Protected(Factions.ByKey(q.Home), ownerTag, deadName, worldPos, ownerGo)) continue;
                if (q.Type == QType.Kill) hit = KillMatches(q.Target, ownerName, deadName) && (!q.HasPos || Game.HDist(rel, q.Pos) <= q.Radius);
                else if (q.Type == QType.Clear) hit = q.HasPos && Game.HDist(rel, q.Pos) <= q.Radius;
                else if (q.Type == QType.Takeover) hit = q.HasPos && Util.Norm(ownerTag) == Util.Norm(q.Target) && Game.HDist(rel, q.Pos) <= q.Radius;
                if (!hit) continue;

                q.Progress++;
                nextRefresh = 0f;
                if (q.Type == QType.Takeover && q.Count <= 0) q.Count = q.Progress + Takeover.CountDefenders(Game.Abs(q.Pos), q.Target, q.Radius);   // head count was not known yet
                // camp has nobody left (it had fewer people than the quest asked for): the camp is cleared
                if (q.Type == QType.Takeover && q.Progress < q.Count && Takeover.CountDefenders(Game.Abs(q.Pos), q.Target, q.Radius) == 0)
                    q.Count = q.Progress;
                if (q.Progress >= q.Count)
                {
                    q.Progress = q.Count;
                    q.ObjectiveDone = true;
                    if (q.Type == QType.Takeover)
                    {
                        Quest tq = q;
                        Defer(delegate
                        {
                            int sent = Takeover.Begin(tq, rng);
                            offers.Clear();     // offers waiting on NPCs may point at the camp that just changed hands
                            Faction win = Factions.ByKey(tq.Home);
                            AddToast(sent > 0 && win != null ? "The " + tq.Label + " is yours. " + sent + " of " + win.Name + "'s people are on their way." : "The " + tq.Label + " is yours.");
                        });
                    }
                    if (!q.ReturnToGiver) { if (Pay(q)) Complete(q); }
                    else AddToast("Objective complete: " + q.Title + " (return to the " + q.GiverName + ")");
                }
                else AddToast(q.Title + ": " + q.Progress + "/" + q.Count);
            }
        }

        // ------------------------------------------------------------------
        // Dialogue
        // ------------------------------------------------------------------
        string Format(string tpl, Quest q, int have)
        {
            if (string.IsNullOrEmpty(tpl)) return "";
            string dist = "?", dir = "";
            Transform pl = Game.Player();
            if ((q.Type == QType.Goto || q.Type == QType.Takeover || q.Type == QType.Deliver) && q.HasPos && pl != null)
            {
                Vector3 t = Game.Abs(q.Pos);
                dist = ((int)Game.HDist(pl.position, t)).ToString();
                dir = Util.Compass(t.x - pl.position.x, t.z - pl.position.z);
            }
            else if (q.Type == QType.Talk) dist = ((int)q.MinDistance).ToString();

            string whereText = "";
            if (pl != null && tpl.IndexOf("{where}", StringComparison.Ordinal) >= 0 && book.Active.Contains(q))
            {
                int haveNow;
                if (q.Type != QType.Fetch || !haveCache.TryGetValue(q.Id, out haveNow)) haveNow = have;
                AimInfo am = Aim(q, haveNow, pl);
                if (am.Has) whereText = Sentence(am, pl);
            }
            if (CountUnknown(q) && (tpl == q.Remind || tpl == q.Done) && !q.ObjectiveDone)
                return "You haven't even counted them yet. Get close to the " + q.Label + " and see what's there.";
            if (whereText.Length == 0) tpl = tpl.Replace(" {where}", "");
            return tpl.Replace("{n}", CountUnknown(q) ? "every one" : q.Count.ToString())
                      .Replace("{hours}", HoursText(DeadlineHours(q)))
                      .Replace("{item}", q.Label)
                      .Replace("{target}", q.Label)
                      .Replace("{poi}", q.Label)
                      .Replace("{reward}", q.Reward.ToString())
                      .Replace("{have}", have.ToString())
                      .Replace("{progress}", q.Progress.ToString())
                      .Replace("{giver}", q.GiverName)
                      .Replace("{dist}", dist)
                      .Replace("{dir}", dir)
                      .Replace("{where}", whereText)
                      .Replace("{radius}", ((int)q.Radius).ToString())
                      .Replace("{place}", q.Place ?? "")
                      .Replace("{minutes}", HoursText(Math.Max(0f, q.TimeLeft) / 3600f));
        }

        readonly HashSet<string> seenNpcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // With [Debug] RecordNpcNames on: logs each NPC you talk to and keeps running lists in npc_names.txt and
        // npc_locations.txt next to the DLL (use these words in the 'giver' key of quests.txt).
        void RecordNpcName(string npc)
        {
            if (!recordNpcs.Value) return;
            try
            {
                if (string.IsNullOrEmpty(npc)) return;
                Log.LogInfo("Talking to NPC: \"" + npc + "\"");
                if (seenNpcs.Add(npc))
                {
                    string path = Path.Combine(DataDir, "npc_names.txt");
                    var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (File.Exists(path)) foreach (string l in File.ReadAllLines(path)) known.Add(l.Trim());
                    if (known.Add(npc)) File.AppendAllText(path, npc + Environment.NewLine);
                }
            }
            catch (Exception e) { Log.LogWarning("Could not record NPC name: " + e.Message); }
            try
            {
                // which camp / cave / building is this NPC standing at? (helps you fill in factions.txt)
                Transform t = bridge.Npc;
                if (t == null || string.IsNullOrEmpty(npc)) return;
                List<Poi> near = Game.FindPois(t.position, 0f, 150f, "");
                if (near.Count == 0) return;
                Poi best = near[0];
                float bd = Game.HDist(best.Pos, t.position);
                for (int i = 1; i < near.Count; i++)
                {
                    float d = Game.HDist(near[i].Pos, t.position);
                    if (d < bd) { bd = d; best = near[i]; }
                }
                string entry = npc + " @ " + best.Name;
                Faction nf = Factions.OfObject(t.gameObject.tag, npc, t.position);
                Log.LogInfo("NPC location: " + entry + " (" + (int)bd + " m)  faction: " + (nf != null ? nf.Name : "none"));
                string lpath = Path.Combine(DataDir, "npc_locations.txt");
                var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(lpath)) foreach (string l in File.ReadAllLines(lpath)) have.Add(l.Trim());
                if (have.Add(entry)) File.AppendAllText(lpath, entry + Environment.NewLine);
            }
            catch (Exception e) { Log.LogWarning("Could not record NPC name: " + e.Message); }
        }

        internal void OnTalkOpen(object inst)
        {
            if (inst != null) bridge.Bind(inst);
            pendingOffer = null;
            repMenuOpen = false;
            uiDirty = true;
            Factions.Refresh(factionsPath, delegate (string w) { Log.LogWarning(w); });
            RecordNpcName(bridge.NpcName);
            if (!ready) return;

            Transform pl = Game.Player();
            if (pl == null) return;
            string npc = bridge.NpcName;
            Quest[] snapshot = book.Active.ToArray();
            if (TryDeliver(snapshot, npc, pl)) return;
            for (int i = 0; i < snapshot.Length; i++)
            {
                Quest q = snapshot[i];
                if (q.Type != QType.Talk) continue;
                if (q.Target != "*" && !Util.NameMatches(q.Target, npc)) continue;
                if (!Factions.Friendly(Factions.ByKey(q.Home), Factions.OfObject(bridge.Npc != null ? bridge.Npc.gameObject.tag : "", npc, bridge.Npc != null ? bridge.Npc.position : pl.position))) continue;   // no carrying word to a rival faction
                if (Game.HDist(pl.position, Game.Abs(q.Start)) < q.MinDistance) continue;
                if (Game.MoneyVar(moneyVar.Value) == null) { AddToast("Could not pay the reward: money variable not found (see the BepInEx log)."); break; }
                bridge.Say(Format(q.Done, q, 0)); Speak(q, "done");
                if (Pay(q)) Complete(q);
                break;
            }
        }

        internal void OnTalkClose()
        {
            pendingOffer = null;
            repMenuOpen = false;
            wantButtons.Clear();
            bridge.ClearButtons();
        }

        // ---- the quest choices in Talk's window -------------------------------------------------

        bool uiDirty;
        readonly List<TalkBtn> wantButtons = new List<TalkBtn>();

        TalkBtn Btn(string key, string text, Action act)
        {
            int generation = bridge.Generation;
            bool queued = false;
            var b = new TalkBtn();
            b.Key = key;
            b.Text = text;
            b.Click = delegate { if (queued) return; queued = true; Defer(delegate { try { if (generation != bridge.Generation || !bridge.TalkingNow || !ready) return; act(); uiDirty = true; } finally { queued = false; } }); };
            return b;
        }

        // Every frame while talking: make sure our choices are in Talk's list (Talk throws its list away and rebuilds it
        // whenever the player picks one of its own prompts, so they have to be put back).
        void SyncTalkUi()
        {
            if (!ready || !bridge.TalkingNow) return;
            bool rebuilt = uiDirty;
            if (uiDirty) { BuildButtons(); uiDirty = false; }
            FilterCrewButtons();
            if ((rebuilt && bridge.NativeOpen) || bridge.NeedsSync(wantButtons)) bridge.SyncButtons(wantButtons);
        }

        void BuildButtons()
        {
            wantButtons.Clear();
            string npc = bridge.NpcName;
            Transform tr = bridge.Npc;
            if (pendingOffer != null)
            {
                wantButtons.Add(Btn("accept", "Accept: " + pendingOffer.Title + "  (" + pendingOffer.Reward + " nuts)", AcceptOffer));
                wantButtons.Add(Btn("decline", "Not interested.", DeclineOffer));
                return;
            }
            if (repMenuOpen)
            {
                try { if (RepShopFaction(npc, tr) != null && Game.GetMoney(moneyVar.Value) >= 0) AddRepButtons(npc, tr); } catch (Exception e) { LogOnce("RepButtons", e); }
                wantButtons.Add(Btn("repback", "Back", delegate { repMenuOpen = false; }));
                return;
            }
            Quest[] mine = book.Active.ToArray();
            for (int i = 0; i < mine.Length; i++)
            {
                Quest q = mine[i];
                if (q.Type == QType.Talk || !IsMyQuest(q, npc, tr)) continue;
                Quest cq = q;
                if (q.Type == QType.Deliver)
                {
                    wantButtons.Add(Btn("back:" + q.Id, "Give back the goods: " + q.Title, delegate { ReturnGoods(cq); }));
                    wantButtons.Add(Btn("drop:" + q.Id, "Abandon: " + q.Title, delegate { AbandonDelivery(cq); }));
                    continue;
                }
                wantButtons.Add(Btn("turn:" + q.Id, "Report in: " + q.Title, delegate { TurnIn(cq); }));
                wantButtons.Add(Btn("drop:" + q.Id, "Abandon: " + q.Title, delegate { Abandon(cq); }));
            }
            wantButtons.Add(Btn("work", "Any work for me?", AskForWork));
            try { AddHireButtons(npc, tr); } catch (Exception e) { LogOnce("HireButtons", e); }
            try
            {
                if (RepShopFaction(npc, tr) != null && Game.GetMoney(moneyVar.Value) >= 0)
                {
                    wantButtons.Add(Btn("repmenu", "Buy or sell reputation...", delegate { repMenuOpen = true; }));
                }
            }
            catch (Exception e) { LogOnce("RepButtons", e); }
        }

        bool repMenuOpen;

        // ---- hired help -----------------------------------------------------------------------------------------

        void AddHireButtons(string npc, Transform tr)
        {
            if (!Hire.Enabled || tr == null) return;
            if (Hire.IsFollower(tr))
            {
                wantButtons.Add(Btn("dismiss", "Dismiss: you're free to go.", delegate { DismissFollower(); }));
                return;
            }
            int cost = Hire.CostFor(npc);
            if (cost < 0) return;
            Faction f = Factions.OfPerson(tr.gameObject.tag, npc, tr.position);
            if (f == null) { if (logged.Add("HireNoFaction" + Game.BaseName(npc))) Log.LogInfo("Hire: " + npc + " (tag " + tr.gameObject.tag + ") has no faction here (no matching tag, factions.txt entry or camp nearby), so it cannot be hired."); return; }
            string shown = Game.BaseName(npc);
            wantButtons.Add(Btn("hire", "Hire " + shown + " for a day  (" + cost + " " + f.Name + " reputation)", delegate { HireNpc(f, cost, tr, shown); }));
        }

        void HireNpc(Faction f, int cost, Transform tr, string shown)
        {
            if (tr == null || (Services.FollowerVehicles != null && Services.FollowerVehicles.IsSeated(tr.gameObject))) return;
            bridge.You("I could use some help. What would it take to have you along?");
            if (tr == null) return;
            string missing = Hire.MissingMods();
            if (missing.Length > 0) { bridge.Say("(Hiring needs " + missing + " installed. See the README.)"); return; }
            if (Hire.Active) { bridge.Say("You already have some help, come back later when you are alone."); return; }
            if (RepOf(f.Key) < cost)
            {
                bridge.Say("I don't walk with just anyone. " + f.Name + " has to think more of you first: " + cost + " reputation, and you have " + Math.Max(0, RepOf(f.Key)) + ".");
                return;
            }
            ChangeRep(f.Key, -cost, null);
            Hire.Start(tr.gameObject, f.Key, shown);
            bridge.Say("Deal. I'm with you for a day. Lead the way.");
        }

        // the recorded voice line for a quest, if any voice actor made one (see Voice.cs)
        void Speak(Quest q, string line)
        {
            Voice.Say(q, line, bridge.Npc, bridge.NpcName);
        }

        void DismissFollower()
        {
            if (PartyQuests.DismissRequested()) return;
            bridge.You("Thanks, I can manage from here.");
            bridge.Say("Suit yourself. I'll see myself back.");
            Hire.Dismiss("You sent {name} away.");
        }

        // ---- the reputation vendor ----------------------------------------------------------------

        static readonly int[] RepSteps = { 1, 5, 25, 50 };

        // the faction whose reputation this NPC trades, or null
        Faction RepShopFaction(string npc, Transform tr)
        {
            if (!repShopOn.Value || !Util.NameMatches(repShopNpcs.Value, npc) || repShopNpcs.Value.Trim().Length == 0) return null;
            Transform pl = Game.Player();
            Vector3 pos = tr != null ? tr.position : (pl != null ? pl.position : Vector3.zero);
            Faction f = Factions.OfObject(tr != null ? tr.gameObject.tag : "", npc, pos);
            return f != null && !f.Hostile ? f : null;
        }

        // how much reputation can be bought / sold right now
        void RepLimits(Faction f, out int canBuy, out int canSell)
        {
            int rep = RepOf(f.Key);
            int cap = Math.Max(1, repMaxDeal.Value);
            int buyP = Math.Max(0, repBuyPrice.Value);
            int nuts = Game.GetMoney(moneyVar.Value);
            int room = Math.Max(0, RepMax - rep);
            int afford = nuts < 0 ? 0 : (buyP > 0 ? nuts / buyP : room);
            canBuy = Math.Min(Math.Min(room, afford), cap);
            canSell = rep > 0 ? Math.Min(rep, cap) : 0;
        }

        void AddRepButtons(string npc, Transform tr)
        {
            Faction f = RepShopFaction(npc, tr);
            if (f == null || Game.GetMoney(moneyVar.Value) < 0) return;
            int canBuy, canSell;
            RepLimits(f, out canBuy, out canSell);
            int buyP = Math.Max(0, repBuyPrice.Value), sellP = Math.Max(0, repSellPrice.Value);
            for (int i = 0; i < RepSteps.Length; i++)
            {
                int n = RepSteps[i];
                if (n > canBuy) break;
                wantButtons.Add(Btn("repbuy:" + n, "Buy " + n + " " + f.Name + " reputation  (" + (n * buyP) + " nuts)", delegate { DealRep(f, n, true); }));
            }
            if (canBuy >= 2)
            {
                int n = canBuy;
                wantButtons.Add(Btn("repbuy:all:" + n, "Buy all you can: " + n + " " + f.Name + " reputation  (" + (n * buyP) + " nuts)", delegate { DealRep(f, n, true); }));
            }
            if (canSell <= 0) return;                    // nothing to sell at 0 or below
            for (int i = 0; i < RepSteps.Length; i++)
            {
                int n = RepSteps[i];
                if (n > canSell) break;
                wantButtons.Add(Btn("repsell:" + n, "Sell " + n + " " + f.Name + " reputation  (+" + (n * sellP) + " nuts)", delegate { DealRep(f, n, false); }));
            }
            if (canSell >= 2)
            {
                int n = canSell;
                wantButtons.Add(Btn("repsell:all:" + n, "Sell all: " + n + " " + f.Name + " reputation  (+" + (n * sellP) + " nuts)", delegate { DealRep(f, n, false); }));
            }
        }

        void DealRep(Faction f, int n, bool buy)
        {
            int canBuy, canSell;
            RepLimits(f, out canBuy, out canSell);            // the numbers may have changed since the buttons were made
            int buyP = Math.Max(0, repBuyPrice.Value), sellP = Math.Max(0, repSellPrice.Value);
            if (buy)
            {
                bridge.You("I'll buy " + n + " reputation with " + f.Name + ".");
                if (n < 1 || n > canBuy) { bridge.Say("You can't afford that, or there's no more to buy."); return; }
                if (!Game.AddMoney(-n * buyP, moneyVar.Value)) { bridge.Say("(Mod problem: the money variable was not found. See the BepInEx log.)"); return; }
                ChangeRep(f.Key, n, null);
                bridge.Say("Done. " + n + " reputation, " + (n * buyP) + " nuts. Word will get round.");
                Log.LogInfo("Bought " + n + " " + f.Name + " reputation for " + (n * buyP) + " nuts (now " + RepOf(f.Key) + ").");
            }
            else
            {
                bridge.You("I'll sell " + n + " reputation with " + f.Name + ".");
                if (n < 1 || n > canSell) { bridge.Say("You haven't got that much to sell."); return; }
                ChangeRep(f.Key, -n, null);
                Game.AddMoney(n * sellP, moneyVar.Value);
                bridge.Say("Done. " + n + " reputation off your name, " + (n * sellP) + " nuts for you.");
                Log.LogInfo("Sold " + n + " " + f.Name + " reputation for " + (n * sellP) + " nuts (now " + RepOf(f.Key) + ").");
            }
        }

        bool ActiveHasId(string id)
        {
            for (int i = 0; i < book.Active.Count; i++) if (book.Active[i].Id == id) return true;
            return false;
        }

        // quests that must be done before this one may be offered (a chain step), compared without regard to case
        bool PrerequisitesDone(Quest d)
        {
            if (string.IsNullOrEmpty(d.After)) return true;
            foreach (string part in d.After.Split(','))
            {
                string id = part.Trim();
                if (id.Length == 0) continue;
                bool found = false;
                foreach (string done in book.DoneIds) if (string.Equals(done, id, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }

        // How many of an item (a quest target token) exist within ItemRange of a point; int.MaxValue when the check is off.
        readonly Dictionary<string, int> itemsAroundCache = new Dictionary<string, int>();
        int ItemsAround(string token, Vector3 center)
        {
            if (itemRange.Value <= 0f) return int.MaxValue;
            int n;
            if (itemsAroundCache.TryGetValue(token, out n)) return n;
            n = Game.CountStacks(Game.FindItems(token, center, itemRange.Value, true));
            itemsAroundCache[token] = n;
            return n;
        }

        Quest MakeOffer(string npc, Transform npcTr)
        {
            itemsAroundCache.Clear();
            Transform pl = Game.Player();
            Vector3 playerRel = pl != null ? Game.Rel(pl.position) : Vector3.zero;
            Vector3 giverRel = npcTr != null ? Game.Rel(npcTr.position) : playerRel;

            blockedNow = BlockedKeys(GiverKey(npcTr, npc), giverRel);
            var eligible = new List<Quest>();
            for (int i = 0; i < defs.Count; i++)
            {
                Quest d = defs[i];
                if (!d.GiverMatches(npc)) continue;
                if (blockedNow.Contains("id:" + d.Id)) continue;       // a neighbour offers this one
                if (d.Once && book.DoneIds.Contains(d.Id)) continue;
                if (ActiveHasId(d.Id)) continue;
                if (!PrerequisitesDone(d)) continue;                    // a later step of a chain whose earlier steps are not done
                if (IsChainStep(d) && ChainOfferedElsewhere(d, GiverKey(npcTr, npc))) continue;   // someone else offered it in the last day
                eligible.Add(d);
            }

            // what really exists around the giver
            Vector3 giverWorld = npcTr != null ? npcTr.position : (pl != null ? pl.position : Vector3.zero);
            float range = questRange.Value;
            Factions.Refresh(factionsPath, delegate (string w) { Log.LogWarning(w); });
            people = Game.FindPeople(Factions.PeopleWords);          // who is around right now
            Faction gf = Factions.OfObject(npcTr != null ? npcTr.gameObject.tag : "", npc, giverWorld);
            if (gf != null && gf.Hostile) { Log.LogInfo("Giver faction " + gf.Name + " is hostile; no quest offered."); return null; }
            offerGiver = npcTr != null ? npcTr.gameObject : null;
            List<EnemyInfo> foes = NotProtected(gf, Game.FindEnemies(giverWorld, range > 0f ? range : 1e9f), offerGiver);
            List<Poi> allPois = FreePois(gf, Game.FindPois(giverWorld, gotoMin.Value, gotoMax.Value, ""));
            List<Poi> pois = Unfound(gf, allPois);                      // places nobody has looked at, not held by the giver's side
            List<Poi> own = gf != null ? OwnCamps(gf, giverWorld) : new List<Poi>();

            bool genOn = generated.Value && anyGen;
            Quest q = null;
            QuestGen.ItemsAround = itemRange.Value > 0f ? (Func<string, int>)(delegate (string tok) { return ItemsAround(tok, giverWorld); }) : null;

            // a quest chain step (the first one too) is always offered before anything else, when this NPC may give it
            var followUps = new List<Quest>();
            for (int i = 0; i < eligible.Count; i++) if (IsChainStep(eligible[i])) followUps.Add(eligible[i]);
            while (q == null && followUps.Count > 0)
            {
                Quest d = followUps[rng.Next(followUps.Count)];
                followUps.Remove(d);
                eligible.Remove(d);
                Quest c = d.Clone();
                if (PrepareHandWritten(c, giverRel, giverWorld, gf))
                {
                    string dk = QuestGen.KeyOf(c);
                    if (dk.Length == 0 || !blockedNow.Contains(dk)) q = c;
                }
            }

            bool useDef = q == null && eligible.Count > 0 && (!genOn || rng.NextDouble() < 0.6);
            if (useDef)
            {
                while (q == null && eligible.Count > 0)
                {
                    int pick = rng.Next(eligible.Count);
                    Quest c = eligible[pick].Clone();
                    eligible.RemoveAt(pick);
                    if (PrepareHandWritten(c, giverRel, giverWorld, gf))
                    {
                        string dk = QuestGen.KeyOf(c);
                        if (dk.Length == 0 || !blockedNow.Contains(dk)) q = c;
                    }
                }
            }
            if (q == null && genOn && allowTakeover && gf != null && rng.NextDouble() < 0.25)
            {
                Poi camp = Takeover.PickCamp(gf, giverWorld, "", takeoverMin.Value, takeoverMax.Value, book.Active, rng, blockedNow);
                Faction owner = camp != null ? Factions.OwnerOfPoi(camp) : null;
                if (camp != null && owner != null)
                {
                    q = new Quest();
                    q.Id = "gen:" + rng.Next(0x7fffffff).ToString("x");
                    q.Once = false;
                    q.Giver = "*";
                    q.Count = 0;                                        // filled in with the camp's real head count if it is in view, else found out later
                    Takeover.Fill(q, camp, owner, takeoverRadius.Value);
                    int est = q.Count > 0 ? q.Count : Math.Max(1, takeoverKills.Value);
                    q.Reward = Math.Max(10, (int)(Math.Round((150f + est * 50f + Game.HDist(giverWorld, camp.Pos) * 0.03f) / 5.0) * 5.0));
                    Phrases.FillDefaults(q, rng);
                }
            }
            if (q == null && genOn && allowDeliver && gf != null && rng.NextDouble() < 0.25)
                q = QuestGen.MakeDelivery(npc, rng, gf, giverWorld, deliverMin.Value, deliverMax.Value, book.Active, blockedNow,
                                          deliverMinutesBase.Value * 60f, deliverMinutesPerKm.Value * 60f, deliverRadius.Value);
            if (q == null && genOn)
            {
                q = QuestGen.Generate(npc, rng, giverRel, playerRel, allowedGen, pois, own, foes, range, blockedNow);
                if (q != null && q.Type == QType.Kill && range <= 0f) q.HasPos = false;
            }
            if (q == null) return null;

            FinishOffer(q, npc, npcTr, gf, giverRel, playerRel);
            return q;
        }

        // The part every new offer shares: who gave it, where, and what it pays (reputation changes the pay).
        void FinishOffer(Quest q, string npc, Transform npcTr, Faction gf, Vector3 giverRel, Vector3 playerRel)
        {
            q.GiverName = npc;
            q.GiverId = npcTr != null ? npcTr.gameObject.name : "";      // only this NPC answers for the job (see IsMyQuest)
            q.HasGiverPos = true; q.GiverPos = giverRel;
            q.Home = gf != null ? gf.Key : "";
            q.Start = playerRel;
            int baseReward = Math.Max(1, (int)Math.Round(q.Reward * rewardMult.Value));
            if (q.RepGain <= 0) q.RepGain = QuestGen.RepFor(q, baseReward);
            q.Reward = Math.Max(1, (int)Math.Round(baseReward * PayMultiplier(q.Home)));
            Remember(q, GiverKey(npcTr, npc), giverRel);
        }

        // ---- has the player found this place? ----------------------------------------------
        List<Vector3> visitedCache = new List<Vector3>();
        float visitedAt = -100f;

        bool IsFound(Vector3 world)
        {
            if (Time.unscaledTime - visitedAt > 5f)
            {
                visitedAt = Time.unscaledTime;
                try { visitedCache = Game.VisitedIcons(); } catch (Exception e) { LogOnce("VisitedIcons", e); visitedCache = new List<Vector3>(); }
            }
            return Game.NearAny(visitedCache, world, 40f) || MapLink.IsExplored(world);
        }

        // places the player has not been to or uncovered on the map, and that the giver's own side does not hold
        List<Poi> Unfound(Faction gf, List<Poi> all)
        {
            var res = new List<Poi>();
            for (int i = 0; i < all.Count; i++)
            {
                Faction owner = Factions.OwnerOfPoi(all[i]);
                if (gf != null && owner != null && owner.Key == gf.Key) continue;       // theirs: that is a check-up, not a discovery
                if (IsFound(all[i].Pos)) continue;
                res.Add(all[i]);
            }
            return res;
        }

        // camps the giver's own faction holds (other than the one they stand in), for check-ups
        List<Poi> OwnCamps(Faction gf, Vector3 giverWorld)
        {
            var res = new List<Poi>();
            foreach (Poi p in Game.FindPois(giverWorld, Math.Max(150f, gotoMin.Value), gotoMax.Value, "camp"))
            {
                if (!p.Name.StartsWith("Camp", StringComparison.OrdinalIgnoreCase)) continue;
                Faction owner = Factions.OwnerOfPoi(p);
                if (owner == null || owner.Key != gf.Key) continue;
                Vector3 rel = Game.Rel(p.Pos);
                bool taken = false;
                for (int j = 0; j < book.Active.Count; j++)
                {
                    Quest a = book.Active[j];
                    if (a.Type == QType.Goto && a.HasPos && Game.HDist(a.Pos, rel) < 5f) { taken = true; break; }
                }
                if (!taken) res.Add(p);
            }
            return res;
        }

        List<EnemyInfo> people = new List<EnemyInfo>();
        GameObject offerGiver;      // the NPC an offer is being made for (never a target of its own job)

        static List<EnemyInfo> NotProtected(Faction gf, List<EnemyInfo> all, GameObject giver)
        {
            var res = new List<EnemyInfo>();
            for (int i = 0; i < all.Count; i++)
            {
                if (giver != null && all[i].Go == giver) continue;
                if (!Factions.Protected(gf, all[i].Tag, all[i].Name, all[i].Pos, all[i].Go)) res.Add(all[i]);
            }
            return res;
        }

        // Removes points of interest the giver's faction may not use, and ones an active go-to quest already points at.
        List<Poi> FreePois(Faction gf, List<Poi> all)
        {
            var res = new List<Poi>();
            for (int i = 0; i < all.Count; i++)
            {
                if (!Factions.PoiAllowed(gf, all[i], people)) continue;
                Vector3 rel = Game.Rel(all[i].Pos);
                bool taken = false;
                for (int j = 0; j < book.Active.Count; j++)
                {
                    Quest a = book.Active[j];
                    if (a.Type == QType.Goto && a.HasPos && Game.HDist(a.Pos, rel) < 5f) { taken = true; break; }
                }
                if (!taken) res.Add(all[i]);
            }
            return res;
        }

        // Fills in a hand-written quest. Returns false when it cannot be done right now
        // (no matching enemy near the giver, no point of interest of the wanted kind).
        bool PrepareHandWritten(Quest q, Vector3 giverRel, Vector3 giverWorld, Faction gf)
        {
            float d = Mathf.Lerp(q.DistMin, q.DistMax, (float)rng.NextDouble());

            // optional conditions that work for every quest type
            if (!string.IsNullOrEmpty(q.NeedPoi))
            {
                string pk = q.NeedPoi;
                if (pk == "*" || Util.Norm(pk) == "any") pk = "";
                if (FreePois(gf, Game.FindPois(giverWorld, 0f, q.NeedRange, pk)).Count == 0) return false;
            }
            if (!string.IsNullOrEmpty(q.NeedEnemy))
            {
                int near = 0;
                List<EnemyInfo> around = Game.FindEnemies(giverWorld, q.NeedRange);
                for (int i = 0; i < around.Count; i++)
                    if (KillMatches(q.NeedEnemy, around[i].Name, around[i].Name)) near++;
                if (near < q.NeedCount) return false;
            }

            switch (q.Type)
            {
                case QType.Fetch:
                    return ItemsAround(q.Target, giverWorld) >= Math.Max(1, q.Count);      // only things that exist near the giver
                case QType.Goto:
                    {
                        string kind = q.Target;
                        if (kind == "*" || Util.Norm(kind) == "any") kind = "";
                        List<Poi> list0 = FreePois(gf, Game.FindPois(giverWorld, q.DistMin, q.DistMax, kind));
                        List<Poi> list;
                        if (q.Checkup)
                        {
                            // a check-up: only on a camp the giver's own faction holds
                            list = new List<Poi>();
                            if (gf == null) return false;
                            for (int li = 0; li < list0.Count; li++)
                            {
                                Faction ow = Factions.OwnerOfPoi(list0[li]);
                                if (ow != null && ow.Key == gf.Key && list0[li].Name.StartsWith("Camp", StringComparison.OrdinalIgnoreCase)) list.Add(list0[li]);
                            }
                        }
                        else list = Unfound(gf, list0);                   // only places nobody has found yet, never the giver's own camps
                        Poi near1 = null;
                        float nd = float.MaxValue;
                        for (int li = 0; li < list.Count; li++)
                        {
                            if (blockedNow != null && blockedNow.Contains(QuestGen.PosKey(Game.Rel(list[li].Pos)))) continue;
                            float ld = Game.HDist(list[li].Pos, giverWorld);
                            if (ld < nd) { nd = ld; near1 = list[li]; }
                        }
                        if (near1 != null)
                        {
                            Poi p = near1;                          // the closest one
                            q.Pos = Game.Rel(p.Pos);
                            q.HasPos = true;
                            if (q.Label == "" || q.Label == "spot") q.Label = p.Kind.ToLowerInvariant();
                            return true;
                        }
                        return false;                                  // nothing suitable in range, so the quest is not offered
                    }
                case QType.Kill:
                    {
                        float range = q.Range < 0f ? questRange.Value : q.Range;
                        int avail = 0;
                        List<EnemyInfo> foes = NotProtected(gf, Game.FindEnemies(giverWorld, range > 0f ? range : 1e9f), offerGiver);
                        if (QuestGen.NoKillQuest(q.Target)) return false;       // no jobs about lizards
                        for (int i = 0; i < foes.Count; i++)
                            if (KillMatches(q.Target, foes[i].Name, foes[i].Name) && !QuestGen.NoKillQuest(foes[i].Name)) avail++;
                        if (avail < Math.Max(1, q.Count)) return false;       // not enough of them around to ask for that many
                        if (range > 0f) { q.HasPos = true; q.Pos = giverRel; q.Radius = range; }
                        return true;
                    }
                case QType.Takeover:
                    {
                        Poi camp = Takeover.PickCamp(gf, giverWorld, q.Target, q.DistMin, q.DistMax, book.Active, rng, blockedNow);
                        if (camp == null) return false;
                        Takeover.Fill(q, camp, Factions.OwnerOfPoi(camp), q.Radius);
                        return true;
                    }
                case QType.Talk:
                    q.MinDistance = d;
                    return true;
                case QType.Deliver:
                    {
                        if (gf == null) return false;                           // goods go to the giver's own people
                        Poi camp = QuestGen.PickDeliveryCamp(gf, giverWorld, q.DistMin, q.DistMax, book.Active, blockedNow, rng);
                        if (camp == null) return false;
                        QuestGen.AimDelivery(q, camp, giverWorld, deliverMinutesBase.Value * 60f, deliverMinutesPerKm.Value * 60f);
                        return true;
                    }
                case QType.Clear:
                    {
                        float cr = q.Radius > 0f ? q.Radius : 250f;
                        int standing = 0;
                        List<EnemyInfo> around = NotProtected(gf, Game.FindEnemies(giverWorld, cr), offerGiver);
                        standing = around.Count;
                        if (standing < Math.Max(1, q.Count)) return false;     // not enough hostiles within range of the camp
                        q.Pos = giverRel;
                        q.HasPos = true;
                        return true;
                    }
            }
            return true;
        }

        // text about how full a container has to be, for fetch quests on cans (empty when the item is not a container)
        string LiquidNote(Quest q)
        {
            if (q.Type != QType.Fetch || minLiquid.Value <= 0f) return "";
            float have;
            float cap = Game.ContainerCapacity(q.Target, out have);
            if (cap <= 0f) return "";
            return " It has to hold at least " + (cap * minLiquid.Value).ToString("0.#") + " L (a full one is " + cap.ToString("0.#") + " L).";
        }

        class Offered { public string Key; public string Giver; public Vector3 Rel; public double Hours; }
        readonly List<Offered> ledger = new List<Offered>();

        static double NowHours()
        {
            double g = Game.GameHours();
            return g >= 0.0 ? g : Time.unscaledTime / 3600.0;
        }

        // keys of quests that other NPCs near this one have offered recently
        HashSet<string> BlockedKeys(string giverKey, Vector3 giverRel)
        {
            var set = new HashSet<string>();
            if (noRepeatMeters.Value <= 0f) return set;
            double now = NowHours();
            for (int i = ledger.Count - 1; i >= 0; i--)
            {
                Offered o = ledger[i];
                double age = now - o.Hours;
                if (age < 0.0 || age > noRepeatHours.Value) { ledger.RemoveAt(i); continue; }
                if (o.Giver == giverKey) continue;
                if (Game.HDist(o.Rel, giverRel) <= noRepeatMeters.Value) set.Add(o.Key);
            }
            return set;
        }

        static bool IsChainStep(Quest d) { return !string.IsNullOrEmpty(d.Chain) || !string.IsNullOrEmpty(d.After); }

        // chain step id -> (NPC that offered it, in-game hour); kept for the session
        readonly Dictionary<string, KeyValuePair<string, double>> chainOffers = new Dictionary<string, KeyValuePair<string, double>>();

        bool ChainOfferedElsewhere(Quest d, string giverKey)
        {
            KeyValuePair<string, double> o;
            if (!chainOffers.TryGetValue(d.Id, out o)) return false;
            double age = NowHours() - o.Value;
            if (age < 0.0 || age >= chainOfferHours.Value) { chainOffers.Remove(d.Id); return false; }
            return o.Key != giverKey;              // the same NPC may offer it again (after its own cooldown)
        }

        void Remember(Quest q, string giverKey, Vector3 giverRel)
        {
            double now = NowHours();
            if (IsChainStep(q) && !string.IsNullOrEmpty(q.Id)) chainOffers[q.Id] = new KeyValuePair<string, double>(giverKey, now);
            string k = QuestGen.KeyOf(q);
            if (k.Length > 0) ledger.Add(new Offered { Key = k, Giver = giverKey, Rel = giverRel, Hours = now });
            if (!q.Id.StartsWith("gen:", StringComparison.Ordinal)) ledger.Add(new Offered { Key = "id:" + q.Id, Giver = giverKey, Rel = giverRel, Hours = now });
        }

        // Is this job one the NPC being talked to answers for? Only the very NPC that gave it (by its object name, which the
        // game keeps across saves). Jobs without a recorded giver (older saves, or started from a dialogue file without a
        // known NPC) fall back to any NPC of that name and faction.
        bool IsMyQuest(Quest q, string npc, Transform tr)
        {
            if (!Util.SameName(q.GiverName, npc)) return false;
            if (!string.IsNullOrEmpty(q.GiverId)) return tr != null && tr.gameObject.name == q.GiverId;
            if (!string.IsNullOrEmpty(q.Home) && tr != null)
            {
                Faction nf = Factions.OfObject(tr.gameObject.tag, npc, tr.position);
                if (nf != null && nf.Key != Util.Norm(q.Home)) return false;
            }
            return true;
        }

        static string GiverKey(Transform tr, string npc) { return tr != null ? tr.gameObject.name : npc; }

        // in-game hours until this NPC will give another quest, or 0. Stamps are game hours * 1000.
        double CooldownLeft(string key)
        {
            long stamp;
            if (CooldownHours <= 0f || !book.GiverStamp.TryGetValue(key, out stamp)) return 0.0;
            double now = Game.GameHours();
            if (now < 0.0) return 0.0;
            double since = now - stamp / 1000.0;
            if (since < 0.0) { book.GiverStamp.Remove(key); return 0.0; }      // an older save was loaded
            double left = CooldownHours - since;
            return left > 0.0 ? left : 0.0;
        }

        void AskForWork()
        {
            if (CrewHistory.Blocked(bridge.Npc)) return;
            string npc = bridge.NpcName;
            Transform npcTr = bridge.Npc;
            bridge.You("Got any work for me?");

            for (int i = 0; i < book.Active.Count; i++)
            {
                Quest mine = book.Active[i];
                if (mine.Type != QType.Talk && IsMyQuest(mine, npc, npcTr))
                {
                    if (mine.Type == QType.Deliver) bridge.Say("You're still carrying my " + mine.Label + ". Get it to the " + mine.Place + ", or give it back.");
                    else bridge.Say("You already have a job from me: " + mine.Title + ". Use \"Report in\" when it's done.");
                    return;
                }
            }
            if (book.Active.Count >= maxActive.Value)
            {
                bridge.Say("You've got enough on your plate. Finish something first.");
                return;
            }

            double wait = CooldownLeft(GiverKey(npcTr, npc));
            if (wait > 0.0)
            {
                bridge.Say("I've got nothing more for you right now. Come back in " + (wait < 1.0 ? "less than an hour" : wait > 23.5 ? "a day" : "about " + Math.Ceiling(wait) + " hours") + ".");
                return;
            }

            int id = npcTr != null ? npcTr.GetInstanceID() : 0;
            Quest offer;
            if (offers.TryGetValue(id, out offer) && offer != null && StaleReason(offer) != null)
            {
                offers.Remove(id);          // the job this NPC had in mind is gone; think of a new one
                offer = null;
            }
            if (offer == null)
            {
                offer = MakeOffer(npc, npcTr);
                if (offer != null) offers[id] = offer;
            }
            if (offer == null)
            {
                bridge.Say(Phrases.Pick(Phrases.NoWork, rng));
                return;
            }
            pendingOffer = offer;
            pendingFor = id;
            uiDirty = true;
            string offerText = Format(offer.Offer, offer, 0);
            if (offer.Type == QType.Kill && offer.HasPos) offerText += " Only the ones within " + (int)offer.Radius + " m of here count.";
            else if (offer.Type == QType.Takeover && offer.HasPos)
                offerText += " It is about " + Format("{dist}", offer, 0) + " m " + Format("{dir}", offer, 0) + " of you.";
            else if (offer.Type == QType.Goto && offer.HasPos && offer.Label != "spot" && !string.IsNullOrEmpty(offer.Label))
                offerText += " It's a " + offer.Label + ", about " + Format("{dist}", offer, 0) + " m " + Format("{dir}", offer, 0) + " of you.";
            offerText += LiquidNote(offer);
            if (offer.Type != QType.Deliver)
            {
                float oh = DeadlineHours(offer);
                if (oh > 0f) offerText += " You have " + HoursText(oh) + " for it.";
            }
            bridge.Say(offerText); Speak(offer, "offer");
        }

        // An offer is kept on the NPC until you answer it, so it can go out of date (the same job taken from someone else,
        // a one-time job finished, the camp already taken). Returns why it can't be accepted any more, or null.
        string StaleReason(Quest q)
        {
            if (book.Active.Contains(q)) return "You already took that one.";
            bool handWritten = !q.Id.StartsWith("gen:", StringComparison.Ordinal);
            if (handWritten && ActiveHasId(q.Id)) return "You're already doing that job for someone.";
            if (handWritten && q.Once && book.DoneIds.Contains(q.Id)) return "Someone already took care of that. Never mind.";
            if ((q.Type == QType.Takeover || q.Type == QType.Goto || q.Type == QType.Deliver) && q.HasPos)
            {
                for (int i = 0; i < book.Active.Count; i++)
                {
                    Quest a = book.Active[i];
                    if (a.Type == q.Type && a.HasPos && Game.HDist(a.Pos, q.Pos) < 10f) return "You're already on that one for someone else.";
                }
            }
            return null;
        }

        void AcceptOffer() { AcceptQuest(pendingOffer, false); }

        // How many in-game hours a new job allows: at least MinimumHours, plus some for the distance involved.
        float DeadlineHours(Quest q)
        {
            if (q.Type == QType.Deliver) return 0f;
            float hours;
            if (q.Hours > 0f) hours = Math.Max(q.Hours, minHours.Value);
            else
            {
                float km = 0f;
                Vector3 from = q.HasGiverPos ? Game.Abs(q.GiverPos) : Game.Abs(q.Start);
                if ((q.Type == QType.Goto || q.Type == QType.Takeover) && q.HasPos) km = Game.HDist(Game.Abs(q.Pos), from) / 1000f;
                else if (q.Type == QType.Talk) km = q.MinDistance / 1000f;
                else if (q.Type == QType.Kill && q.HasPos) km = Math.Min(q.Radius, 4000f) / 1000f;
                hours = Math.Min(Math.Max(minHours.Value, maxHours.Value), minHours.Value + km * extraHoursPerKm.Value);
            }
            return Math.Max(minHours.Value, hours);
        }

        static string HoursText(float h)
        {
            if (h >= 24f)
            {
                int d = (int)(h / 24f);
                int r = (int)Math.Round(h - d * 24f);
                if (r >= 24) { d++; r = 0; }
                return d + (d == 1 ? " day" : " days") + (r > 0 ? " " + r + " h" : "");
            }
            return (int)Math.Ceiling(h) + " h";
        }

        void AssignDeadline(Quest q)
        {
            if (q.Type == QType.Deliver) return;
            double now = Game.GameHours();
            if (now < 0.0) { q.Deadline = 0.0; return; }
            q.Deadline = now + DeadlineHours(q);
        }

        // Takes a job: from the offer that was just accepted, or (scripted = true) started by a dialogue file.
        void AcceptQuest(Quest q, bool scripted)
        {
            if (CrewHistory.Blocked(bridge.Npc)) return;
            if (q == null) return;
            if (!scripted) { pendingOffer = null; offers.Remove(pendingFor); }
            string stale = StaleReason(q);
            if (stale != null)
            {
                bridge.Say(stale);
                return;
            }
            if (book.Active.Count >= maxActive.Value)
            {
                bridge.Say("You've got enough on your plate.");
                return;
            }
            if (q.Type == QType.Deliver)
            {
                // the goods appear on the ground in front of the giver; the clock starts now
                int made = Game.SpawnItemsInFront(q.Target, q.Count, bridge.Npc, delegate (string m) { Log.LogInfo(m); });
                if (made <= 0) { bridge.Say("Hm. I can't find the goods. Forget it."); return; }
                q.Count = made;
                QuestGen.ResetClock(q, bridge.Npc != null ? bridge.Npc.position : Vector3.zero, deliverMinutesBase.Value * 60f, deliverMinutesPerKm.Value * 60f);
            }
            Transform pl = Game.Player();
            if (pl != null) q.Start = Game.Rel(pl.position);
            AssignDeadline(q);
            book.Active.Add(q);
            if (!scripted) StampGiver(bridge.Npc, q.GiverName);
            SetQuestFlags(q, "active");
            bridge.You("I'll do it.");
            bridge.Say(Format(q.Accept, q, 0)); Speak(q, "accept");
            AddToast("New quest: " + q.Title);
            nextRefresh = 0f;
        }

        // this NPC gives out nothing more for a while (after you took a job from them, or turned one down)
        void StampGiver(Transform tr, string npc)
        {
            if (CooldownHours > 0f && Game.GameHours() >= 0.0) book.GiverStamp[GiverKey(tr, npc)] = (long)(Game.GameHours() * 1000.0);
        }

        void DeclineOffer()
        {
            offers.Remove(pendingFor);
            pendingOffer = null;
            StampGiver(bridge.Npc, bridge.NpcName);
            bridge.You("Not interested.");
            bridge.Say(Phrases.Pick(Phrases.Decline, rng) + (CooldownHours > 0f ? " Don't ask again today." : ""));
        }

        // giving a job up at the giver costs a little reputation
        void Abandon(Quest q)
        {
            if (!book.Active.Remove(q)) return;
            Reopen(q);
            bridge.You("I can't do the job: " + q.Title + ".");
            bridge.Say(Phrases.Pick(Phrases.Decline, rng));
            if (!string.IsNullOrEmpty(q.Home) && FailPenalty(q) > 0) ChangeRep(q.Home, -FailPenalty(q), null);
            SetQuestFlags(q, "failed");
            nextRefresh = 0f;
        }

        void TurnIn(Quest q)
        {
            if (!book.Active.Contains(q)) return;      // already handed in (two clicks in one slow frame)
            bridge.You("About the job: " + q.Title + ".");
            Transform pl = Game.Player();
            Vector3 pp = pl != null ? pl.position : Vector3.zero;

            List<ItemHit> hits = null;
            int have = 0;
            if (q.Type == QType.Fetch)
            {
                hits = Game.FindItems(q.Target, pp, handRadius.Value, countCarried.Value);
                have = Game.CountStacks(hits);
                if (have < q.Count)
                {
                    string why = "";
                    if (minLiquid.Value > 0f)
                    {
                        float keep = Game.MinLiquidFraction;
                        Game.MinLiquidFraction = 0f;
                        int anyFill = Game.CountStacks(Game.FindItems(q.Target, pp, handRadius.Value, countCarried.Value));
                        Game.MinLiquidFraction = keep;
                        if (anyFill > have)
                        {
                            float held;
                            Game.ContainerCapacity(q.Target, out held);
                            why = "That's not enough. " + (held > 0.05f ? "That one only has " + held.ToString("0.#") + " L." : "That one is empty.") + LiquidNote(q).TrimStart();
                        }
                    }
                    bridge.Say(why.Length > 0 ? why : Format(q.Remind, q, have));
                    if (why.Length == 0) Speak(q, "remind");
                    return;
                }
            }
            else if (!q.ObjectiveDone)
            {
                bridge.Say(Format(q.Remind, q, 0)); Speak(q, "remind");
                return;
            }

            if (Game.MoneyVar(moneyVar.Value) == null)
            {
                bridge.Say("(Mod problem: the money variable was not found, so I can't pay you. See the BepInEx log.)");
                return;
            }

            // kill jobs: bodies brought back are worth a bonus (the giver takes them away); none is fine too
            int bonus = 0, brought = 0;
            if (q.Type == QType.Kill && bodyBonus.Value > 0f)
            {
                List<GameObject> bodies = FindBodiesFor(q, bridge.Npc != null ? bridge.Npc.position : pp);
                brought = Math.Min(bodies.Count, Math.Max(1, q.Count));
                for (int i = 0; i < brought; i++) if (bodies[i] != null) UnityEngine.Object.Destroy(bodies[i]);
                bonus = (int)Math.Round(q.Reward * bodyBonus.Value * brought);
            }

            if (hits != null) Game.TakeItems(hits, q.Count, bridge.Npc != null ? (Vector3?)bridge.Npc.position : null);
            Speak(q, "done");
            bridge.Say(Format(q.Done, q, q.Count) + (brought > 0 ? " And you brought " + (brought == 1 ? "the body" : brought + " bodies") + ": that's another " + bonus + " nuts." : ""));
            if (Pay(q))
            {
                if (bonus > 0 && Game.AddPartyMoney(bonus, moneyVar.Value)) AddToast("Bodies brought back: +" + bonus + " nuts");
                Complete(q);
            }
        }

        // the bodies of enemies this kill job asked for that lie near `center` (nearest first)
        List<GameObject> FindBodiesFor(Quest q, Vector3 center)
        {
            var res = new List<GameObject>();
            foreach (GameObject go in Game.FindBodies(center, bodyRadius.Value))
            {
                string dead = Game.BaseName(go.name);
                string owner = dead.EndsWith("_Dead", StringComparison.OrdinalIgnoreCase) ? dead.Substring(0, dead.Length - 5) : dead;
                if (KillMatches(q.Target, owner, dead)) res.Add(go);
            }
            return res;
        }

        // ------------------------------------------------------------------
        // Actions from Talk's dialogue files ("action = quest.give:<id>", quest.complete:<id>, quest.abandon:<id>)
        // ------------------------------------------------------------------
        internal void OnTalkAction(string action, string npc)
        {
            try
            {
                if (string.IsNullOrEmpty(action)) return;
                string a = action.Trim();
                int c = a.IndexOf(':');
                string verb = (c >= 0 ? a.Substring(0, c) : a).Trim().ToLowerInvariant();
                string arg = c >= 0 ? a.Substring(c + 1).Trim() : "";
                if (!verb.StartsWith("quest.", StringComparison.Ordinal)) return;
                Defer(delegate { RunTalkAction(verb, arg); uiDirty = true; });
            }
            catch (Exception e) { LogOnce("OnTalkAction", e); }
        }

        Quest ActiveById(string id)
        {
            for (int i = 0; i < book.Active.Count; i++)
                if (string.Equals(book.Active[i].Id, id, StringComparison.OrdinalIgnoreCase)) return book.Active[i];
            return null;
        }

        void RunTalkAction(string verb, string id)
        {
            if (verb == "give" && CrewHistory.Blocked(bridge.Npc)) return;
            if (!ready || id.Length == 0) return;
            switch (verb)
            {
                case "quest.give":
                    {
                        Quest def = null;
                        for (int i = 0; i < defs.Count; i++) if (string.Equals(defs[i].Id, id, StringComparison.OrdinalIgnoreCase)) { def = defs[i]; break; }
                        if (def == null) { Log.LogWarning("quest.give: no quest [" + id + "] in quests.txt."); return; }
                        if (ActiveById(id) != null) return;
                        string npc = bridge.NpcName;
                        Transform npcTr = bridge.Npc;
                        Transform pl = Game.Player();
                        Vector3 playerRel = pl != null ? Game.Rel(pl.position) : Vector3.zero;
                        Vector3 giverRel = npcTr != null ? Game.Rel(npcTr.position) : playerRel;
                        Vector3 giverWorld = npcTr != null ? npcTr.position : (pl != null ? pl.position : Vector3.zero);
                        Factions.Refresh(factionsPath, delegate (string w) { Log.LogWarning(w); });
                        people = Game.FindPeople(Factions.PeopleWords);
                        Faction gf = Factions.OfObject(npcTr != null ? npcTr.gameObject.tag : "", npc, giverWorld);
                        offerGiver = npcTr != null ? npcTr.gameObject : null;
                        blockedNow = new HashSet<string>();
                        itemsAroundCache.Clear();
                        Quest c = def.Clone();
                        if (!PrepareHandWritten(c, giverRel, giverWorld, gf)) { AddToast("The job '" + c.Title + "' can't be given right now (nothing suitable around)."); return; }
                        FinishOffer(c, npc, npcTr, gf, giverRel, playerRel);
                        AcceptQuest(c, true);
                        break;
                    }
                case "quest.complete":
                    {
                        Quest q = ActiveById(id);
                        if (q == null) return;
                        if (Game.MoneyVar(moneyVar.Value) == null) { AddToast("Could not pay the reward: money variable not found (see the BepInEx log)."); return; }
                        if (Pay(q)) Complete(q);
                        break;
                    }
                case "quest.abandon":
                    {
                        Quest q = ActiveById(id);
                        if (q == null) return;
                        CancelQuest(q, "called off");
                        break;
                    }
            }
        }

        // ------------------------------------------------------------------
        // Saving and loading with the game's own save slot (Easy Save)
        // ------------------------------------------------------------------
        static bool IsLoadingState(string s)
        {
            switch (s)
            {
                case "Start":
                case "setSeed":
                case "generateTerrain":
                case "Wait for Terrain Gen":
                case "LoadGame":
                case "generateTerrain 2":
                case "LoadVar":
                    return true;
            }
            return false;
        }

        // Forgets everything that belongs to the world that was just left (offers, who offered what, map markers).
        void ResetSession()
        {
            deferred.Clear(); Voice.Stop();
            pendingOffer = null;
            offers.Clear();
            ledger.Clear();
            chainOffers.Clear();
            lastGameHours = -1.0;       // the next world's clock must not be compared with the old one (deliveries would lose the difference)
            haveCache.Clear();
            campEmptyFor.Clear();
            foreach (var f in QuestWorld.People) if (f != null && madeHostile.Contains(f.gameObject.GetInstanceID())) Game.SetPlayerEnemy(f.gameObject, false);
            madeHostile.Clear();
            _emptyScans.Clear(); aimCache.Clear();
            scouting.Clear();
            wantButtons.Clear();
            bridge.ClearButtons();
            MapLink.Reset();
            Hire.Reset();
        }



        bool ResolveEs3()
        {
            if (es3Resolved) return es3Save != null && es3Load != null && es3Exists != null;
            es3Resolved = true;
            es3Type = FindType("ES3");
            if (es3Type == null) es3Type = Type.GetType("ES3, Assembly-CSharp-firstpass", false);
            if (es3Type == null) { Log.LogError("Easy Save (ES3) was not found; quests cannot be saved."); return false; }

            foreach (MethodInfo m in es3Type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                ParameterInfo[] ps = m.GetParameters();
                if (m.Name == "Save" && m.IsGenericMethodDefinition && ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType.IsGenericParameter)
                { if (es3Save == null) es3Save = m.MakeGenericMethod(typeof(string)); }
                else if (m.Name == "Load" && m.IsGenericMethodDefinition && ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType.IsGenericParameter)
                { if (es3Load == null) es3Load = m.MakeGenericMethod(typeof(string)); }
                else if (m.Name == "KeyExists" && ps.Length == 1 && ps[0].ParameterType == typeof(string))
                { if (es3Exists == null) es3Exists = m; }
            }
            foreach (MethodInfo m in es3Type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                ParameterInfo[] ps = m.GetParameters();
                if (m.Name == "Save" && m.IsGenericMethodDefinition && ps.Length == 3 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType.IsGenericParameter && ps[2].ParameterType.Name == "ES3Settings")
                    es3SaveS = m.MakeGenericMethod(typeof(string));
                else if (m.Name == "Load" && m.IsGenericMethodDefinition && ps.Length == 3 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType.IsGenericParameter && ps[2].ParameterType.Name == "ES3Settings")
                    es3LoadS = m.MakeGenericMethod(typeof(string));
                else if (m.Name == "KeyExists" && !m.IsGenericMethodDefinition && ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType.Name == "ES3Settings")
                    es3ExistsS = m;
            }
            es3SettingsType = es3Type.Assembly.GetType("ES3Settings", false);
            Type locType = es3Type.GetNestedType("Location");
            if (locType != null) { try { es3FileLocation = Enum.Parse(locType, "File"); } catch (Exception) { } }
            Log.LogInfo("Easy Save: default " + (es3Save != null && es3Load != null) + ", straight-to-file " + (es3SaveS != null && es3LoadS != null && es3SettingsType != null && es3FileLocation != null));
            if (es3Save == null || es3Load == null || es3Exists == null)
            {
                Log.LogError("Easy Save has no Save<T>(key,value) / Load<T>(key,default) / KeyExists(key) overloads this mod can use; quests cannot be saved.");
                return false;
            }
            return true;
        }

        // ---- Easy Save's memory cache ---------------------------------------------------------------
        // The game saves through Easy Save's cached file and ends with StoreCachedFile, which rewrites the slot file from
        // memory. A value written only to the file is wiped by that, so it also has to go into the cached copy
        // (the Immersive Map mod does the same: "disk + memory").
        static object StaticMember(Type t, string name)
        {
            const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            PropertyInfo pr = t.GetProperty(name, f);
            if (pr != null) return pr.GetValue(null, null);
            FieldInfo fi = t.GetField(name, f);
            return fi != null ? fi.GetValue(null) : null;
        }

        static object InstMember(object o, string name)
        {
            const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type t = o.GetType();
            PropertyInfo pr = t.GetProperty(name, f);
            if (pr != null) return pr.GetValue(o, null);
            FieldInfo fi = t.GetField(name, f);
            return fi != null ? fi.GetValue(o) : null;
        }

        // the path Easy Save is currently using by default = the slot being played
        static string DefaultEs3Path()
        {
            try
            {
                Type st = FindType("ES3Settings");
                if (st == null) return "";
                object s = StaticMember(st, "defaultSettings");
                if (s == null) return "";
                object path = InstMember(s, "path");
                return path as string ?? "";
            }
            catch (Exception) { return ""; }
        }

        static List<object> CachedSlotFiles()
        {
            var res = new List<object>();
            try
            {
                Type ft = FindType("ES3File");
                string want = DefaultEs3Path();
                if (ft == null || want.Length == 0) return res;
                string wantName = Path.GetFileName(want);
                System.Collections.IDictionary d = StaticMember(ft, "cachedFiles") as System.Collections.IDictionary;
                if (d == null) return res;
                foreach (System.Collections.DictionaryEntry de in d)
                {
                    string k = de.Key as string;
                    if (k != null && de.Value != null && string.Equals(Path.GetFileName(k), wantName, StringComparison.OrdinalIgnoreCase)) res.Add(de.Value);
                }
            }
            catch (Exception) { }
            return res;
        }

        static int SaveToCache(string key, string text)
        {
            int n = 0;
            foreach (object file in CachedSlotFiles())
            {
                try
                {
                    foreach (MethodInfo m in file.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        ParameterInfo[] ps = m.GetParameters();
                        if (m.Name == "Save" && m.IsGenericMethodDefinition && ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType.IsGenericParameter)
                        {
                            m.MakeGenericMethod(typeof(string)).Invoke(file, new object[] { key, text });
                            n++;
                            break;
                        }
                    }
                }
                catch (Exception) { }
            }
            return n;
        }

        static string LoadFromCache(string key)
        {
            foreach (object file in CachedSlotFiles())
            {
                try
                {
                    MethodInfo ex = file.GetType().GetMethod("KeyExists", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(string) }, null);
                    if (ex == null || !(bool)ex.Invoke(file, new object[] { key })) continue;
                    foreach (MethodInfo m in file.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        ParameterInfo[] ps = m.GetParameters();
                        if (m.Name == "Load" && m.IsGenericMethodDefinition && ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType.IsGenericParameter)
                        {
                            string s = m.MakeGenericMethod(typeof(string)).Invoke(file, new object[] { key, "" }) as string;
                            if (!string.IsNullOrEmpty(s)) return s;
                        }
                    }
                }
                catch (Exception) { }
            }
            return "";
        }

        // Settings for one save file, read and written on disk (not Easy Save's in-memory copy). Same approach as Immersive Map.
        object DiskSettings(string file)
        {
            if (es3SettingsType == null || es3FileLocation == null) return null;
            object st = null;
            foreach (ConstructorInfo c in es3SettingsType.GetConstructors())
            {
                ParameterInfo[] ps = c.GetParameters();
                if (ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(Enum[]))
                { st = c.Invoke(new object[] { file, new Enum[] { (Enum)es3FileLocation } }); break; }
            }
            if (st == null)
            {
                foreach (ConstructorInfo c in es3SettingsType.GetConstructors())
                {
                    ParameterInfo[] ps = c.GetParameters();
                    if (ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == es3SettingsType) { st = c.Invoke(new object[] { file, null }); break; }
                }
                if (st == null) return null;
                const BindingFlags I = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                FieldInfo lf = es3SettingsType.GetField("location", I);
                if (lf != null) lf.SetValue(st, es3FileLocation);
                else { PropertyInfo lp = es3SettingsType.GetProperty("location", I); if (lp != null && lp.CanWrite) lp.SetValue(st, es3FileLocation, null); else return null; }
            }
            return st;
        }

        bool SaveToDisk(string key, string value, string file)
        {
            object st = es3SaveS == null ? null : DiskSettings(file);
            if (st == null) return false;
            es3SaveS.Invoke(null, new object[] { key, value, st });
            return true;
        }

        string LoadFromDisk(string key, string file)
        {
            object st = es3LoadS == null ? null : DiskSettings(file);
            if (st == null) return null;
            if (es3ExistsS != null && !(bool)es3ExistsS.Invoke(null, new object[] { key, st })) return null;
            return (string)es3LoadS.Invoke(null, new object[] { key, "", st });
        }

        // the current save slot's file name (SaveGame1.es3 ...), asked from the game's own ES3SettingsMod
        string SlotId()
        {
            string dp = DefaultEs3Path();
            if (dp.Length > 0) return Path.GetFileName(dp);
            try
            {
                Type t = FindType("ES3SettingsMod");
                if (t != null)
                {
                    const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                    MethodInfo m = t.GetMethod("GetSavePath", f, null, Type.EmptyTypes, null);
                    if (m != null) { object r = m.Invoke(null, null); if (r != null) return r.ToString(); }
                    PropertyInfo pr = t.GetProperty("DefaultSavePath", f);
                    if (pr != null) { object r = pr.GetValue(null, null); if (r != null) return r.ToString(); }
                    FieldInfo fi = t.GetField("DefaultSavePath", f);
                    if (fi != null) { object r = fi.GetValue(null); if (r != null) return r.ToString(); }
                }
            }
            catch (Exception) { }
            return "unknown";
        }

        string BackupPath()
        {
            string slot = SlotId();
            var sbn = new StringBuilder();
            for (int i = 0; i < slot.Length; i++) sbn.Append(char.IsLetterOrDigit(slot[i]) ? slot[i] : '_');
            string dir = Path.Combine(Paths.ConfigPath, "ApocalypterQuests_saves");
            return Path.Combine(dir, sbn + ".txt");
        }

        // Cooldown stamps that have run out are no use any more; dropping them keeps the saved data small over a long game.
        void PruneStamps()
        {
            double now = Game.GameHours();
            if (now < 0.0 || book.GiverStamp.Count == 0) return;
            List<string> drop = null;
            foreach (KeyValuePair<string, long> kv in book.GiverStamp)
            {
                double age = now - kv.Value / 1000.0;
                double keep = kv.Key.StartsWith("camp:", StringComparison.Ordinal) ? Takeover.GarrisonCooldownHours : CooldownHours;
                if (age < 0.0 || age > keep + 1.0) { if (drop == null) drop = new List<string>(); drop.Add(kv.Key); }
            }
            if (drop != null) foreach (string k in drop) book.GiverStamp.Remove(k);
        }

        void SaveBook()
        {
            if (!ready || _sessionSlot == null || _sessionSlot != SaveBus.Slot) return;
            if (saveBlocked) { Log.LogWarning("Not saving quests: the data in this slot was in a format this version cannot read."); return; }
            PruneStamps();
            CaptureBook();
            string text = book.Serialize();
            bool es3ok = false, diskOk = false;
            bool haveEs3 = ResolveEs3();
            if (haveEs3)
            {
                string slotFile = DefaultEs3Path();
                if (slotFile.Length > 0)
                {
                    try { diskOk = SaveToDisk(SaveKey, text, slotFile); }
                    catch (Exception e) { Log.LogWarning("Saving quests straight into " + slotFile + " failed: " + (e.InnerException ?? e).Message); }
                }
                try
                {
                    es3Save.Invoke(null, new object[] { SaveKey, text });
                    es3ok = true;
                }
                catch (Exception e) { LogOnce("SaveBook", e is TargetInvocationException && e.InnerException != null ? e.InnerException : e); }
            }
            int mem = SaveToCache(SaveKey, text);
            // a backup file is only written when Easy Save can't be used at all (the only case in which it is read)
            string backup = "";
            if (!haveEs3)
            {
                try
                {
                    string path = BackupPath();
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, text);
                    backup = path;
                }
                catch (Exception e) { LogOnce("SaveBackup", e); }
            }
            Core.Log.Always("[Quests] Saved " + book.Active.Count + " quests (slot " + SlotId() + ", " + text.Length + " chars, game save " + (es3ok || diskOk ? "ok" : "FAILED") + (diskOk ? " (disk" + (es3ok ? " + memory)" : ")") : " (memory only)") + ", cache copies " + mem + (backup.Length > 0 ? ", backup file written" : "") + ").");
        }

        void LoadBook()
        {
            book.Clear();
            saveBlocked = false;
            string data = "";
            string from = "";
            bool readError = false;
            bool es3ok = ResolveEs3();
            if (es3ok)
            {
                string slotFile = DefaultEs3Path();
                if (slotFile.Length > 0)
                {
                    try { string d = LoadFromDisk(SaveKey, slotFile); if (!string.IsNullOrEmpty(d)) { data = d; from = "game save file " + slotFile; } }
                    catch (Exception e) { readError = true; LogOnce("LoadDisk", e is TargetInvocationException && e.InnerException != null ? e.InnerException : e); }
                }
            }
            if (es3ok && string.IsNullOrEmpty(data))
            {
                try
                {
                    bool exists = (bool)es3Exists.Invoke(null, new object[] { SaveKey });
                    if (exists) { data = (string)es3Load.Invoke(null, new object[] { SaveKey, "" }); from = "game save"; }
                }
                catch (Exception e) { readError = true; LogOnce("LoadBook", e is TargetInvocationException && e.InnerException != null ? e.InnerException : e); }
            }
            if (string.IsNullOrEmpty(data))
            {
                string cached = LoadFromCache(SaveKey);
                if (!string.IsNullOrEmpty(cached)) { data = cached; from = "game save (memory)"; }
            }
            // The backup file is NOT per save: using it when a save simply has no quests would load later quests into an
            // older save. It is only a fallback for when Easy Save cannot be used at all.
            if (string.IsNullOrEmpty(data) && !es3ok)
            {
                try
                {
                    string path = BackupPath();
                    if (File.Exists(path)) { data = File.ReadAllText(path); from = "backup file"; }
                }
                catch (Exception e) { readError = true; LogOnce("LoadBackup", e); }
            }
            if (readError) { saveBlocked = true; book.Clear(); Warn("Quest data could not be read; saving is blocked for this session."); return; }
            if (string.IsNullOrEmpty(data)) { Log.LogInfo("No saved quests found for slot " + SlotId() + "."); return; }
            if (!book.Deserialize(data))
            {
                saveBlocked = true;
                book.Clear();
                Log.LogWarning("Quest data in slot " + SlotId() + " (" + from + ") could not be read; leaving it untouched.");
                return;
            }
            RestoreBook();
            MapLink.Reset();
            for (int i = 0; i < book.Active.Count; i++)
                if (book.Active[i].Type != QType.Deliver && book.Active[i].Deadline <= 0.0) AssignDeadline(book.Active[i]);   // jobs saved by an older version get a fresh time limit
            Log.LogInfo("Loaded " + book.Active.Count + " quests from the " + from + " (slot " + SlotId() + ").");
        }

        // ------------------------------------------------------------------
        // HUD
        // ------------------------------------------------------------------
        void EnsureStyles()
        {
            if (trackStyle != null) return;
            trackStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.UpperRight, wordWrap = false, richText = false };
            toastStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = false };
            panelStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, wordWrap = true, richText = true, alignment = TextAnchor.UpperLeft };
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.UpperLeft, richText = false };
        }

        GUIStyle windowStyle;
        float nextSkinTry;

        // the quest log's background: the same image (and 9-slice edge) as Talk's dialogue window, or Talk's own
        // plain colour when it has no image
        void DrawWindow(Rect box)
        {
            Texture2D tex;
            RectOffset border;
            if (windowStyle == null && Time.unscaledTime >= nextSkinTry)
            {
                nextSkinTry = Time.unscaledTime + 2f;
                if (bridge.WindowSkin(out tex, out border))
                {
                    windowStyle = new GUIStyle();
                    windowStyle.normal.background = tex;
                    windowStyle.border = border;
                }
            }
            Color old = GUI.color;
            if (windowStyle != null)
            {
                GUI.color = Color.white;
                if (Event.current.type == EventType.Repaint) windowStyle.Draw(box, false, false, false, false);
            }
            else
            {
                GUI.color = TalkBridge.TalkFallbackColor;
                GUI.DrawTexture(box, Texture2D.whiteTexture);
            }
            GUI.color = old;
        }

        static void Shadowed(Rect r, string text, GUIStyle st)
        {
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, st);
            GUI.color = old;
            GUI.Label(r, text, st);
        }

        internal void Draw()
        {
            if (!ready) return;
            EnsureStyles();
            Matrix4x4 oldMatrix = GUI.matrix;
            try
            {
                float s = Screen.height / 1080f;
                GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
                float vw = Screen.width / s;

                bool paused = GamePaused();              // the pause menu (and the talk window) stop the clock
                if (!TrackerStyled && showTracker.Value && trackerLines.Count > 0 && !showLog && !paused)
                {
                    float y = 24f;
                    for (int i = 0; i < trackerLines.Count && i < 5; i++, y += 26f)
                        Shadowed(new Rect(vw - 760f, y, 740f, 26f), trackerLines[i], trackStyle);
                }

                if (showLog && !JournalStyled && !paused)
                {
                    var box = new Rect(vw / 2f - 400f, 150f, 800f, 620f);
                    DrawWindow(box);
                    panelStyle.normal.textColor = Color.white;
                    titleStyle.normal.textColor = Color.white;
                    GUI.Label(new Rect(box.x + 36f, box.y + 28f, 730f, 40f), "Quests   (" + book.TotalDone + " completed)", titleStyle);
                    GUI.Label(new Rect(box.x + 36f, box.y + 78f, 730f, 510f), logText, panelStyle);
                }

                float ty = 120f;
                for (int i = toasts.Count - 1; i >= 0; i--)
                {
                    if (Time.unscaledTime > toasts[i].Until) { toasts.RemoveAt(i); continue; }
                }
                for (int i = 0; i < toasts.Count; i++, ty += 34f)
                    Shadowed(new Rect(vw / 2f - 500f, ty, 1000f, 34f), toasts[i].Text, toastStyle);
            }
            finally { GUI.matrix = oldMatrix; }
        }
    }
}
