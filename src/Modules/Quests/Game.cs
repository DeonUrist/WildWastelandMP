using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Quests
{
    // One stack of a matching item found in the world or on the player.
    internal class ItemHit
    {
        public GameObject Go;
        public int Stack = 1;
        public FsmInt Qty;       // the item's own "Quantity" variable, if it is a stack
        public bool Held;        // in the hand / a quick slot (under the player camera)
        public bool Reserve;     // not an object: spare rounds in the player's ammo pouch (Qty = that ammo counter)
        public int PerBox = 1;   // rounds that make one "box" for a reserve hit
        public float Dist;
        public string Display = "";
    }

    // A point of interest (camp, cave, building, wreck) in the loaded part of the world.
    internal class Poi
    {
        public string Kind = "";      // "Camp", "Cave", "Building", "Wreck"
        public string Name = "";      // object name without (Clone), e.g. Camp_11
        public string Faction = "";   // from a child object named after a human faction (EnemySpawn_Coyotes_V1, CoyotesMerchantSpawn_V1, ...) ("" = none)
        public Transform Obj;         // the camp object itself
        public Vector3 Pos;           // world position
    }

    // A living enemy near somewhere.
    internal class EnemyInfo
    {
        public string Name = "";      // e.g. Grimhound
        public string Tag = "";       // Unity tag; the game uses it as the faction (Scrapyard, Coyotes)
        public Vector3 Pos;           // world position
        public GameObject Go;         // the object itself (so the quest giver can be left out of its own targets)
    }

    // Everything that touches the game's own objects lives here, so a game update only needs fixes in one file.
    internal static partial class Game
    {

        // ---- world / player ----------------------------------------------------------

        // The game shifts the whole world as you travel; the MapMagic root moves with it.
        public static Vector3 Origin() { return Core.Origin.Anchor; }

        public static Vector3 Rel(Vector3 world) { return world - Origin(); }
        public static Vector3 Abs(Vector3 rel) { return rel + Origin(); }

        public static Transform Player() { var p = GameRefs.Player; return p != null ? p.transform : null; }

        public static float HDist(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ---- points of interest and enemies -------------------------------------------

        static readonly string[] PoiKinds = { "Camp", "Cave", "Building", "Wreck" };

        // The game marks who a camp belongs to with child objects named after the faction: EnemySpawn_Coyotes_V1,
        // CoyotesMerchantSpawn_V1, EnemySpawn_Scrapyard_V1 ... Only human factions (see Factions.HumanFactions) count;
        // the wild / burrower spawners are animals and mutants.
        internal static string SpawnFactionOf(Transform poi)
        {
            for (int i = 0; i < poi.childCount; i++)
            {
                string n = Util.Norm(poi.GetChild(i).name);
                for (int h = 0; h < Factions.HumanFactions.Length; h++)
                {
                    string hf = Factions.HumanFactions[h];
                    if (hf.Length > 0 && n.Contains(hf)) return char.ToUpperInvariant(hf[0]) + hf.Substring(1);
                }
            }
            return "";
        }

        // Base names of the living people (Health FSM in state "health") that carry this faction tag.
        public static List<string> FindNpcNames(string faction)
        {
            var names = new List<string>();
            var all = QuestWorld.People;
            if (all == null) return names;
            string want = Util.Norm(faction);
            for (int i = 0; i < all.Count; i++)
            {
                PlayMakerFSM f = all[i];
                if (f == null || f.FsmName != "Health" || f.ActiveStateName != "health") continue;
                GameObject go = f.gameObject;
                if (go == null || !go.activeInHierarchy || Util.Norm(go.tag) != want) continue;
                names.Add(BaseName(go.name));
            }
            return names;
        }

        // Makes a spawned object part of the game's own save: gives it the next `<name>(Clone)<N>` name from the
        // itemNameID counter and adds it to ArrayList_Items on NewGO_ArrayList, the list the game saves with ES3.
        // Same thing the game's spawners (and Apocaspawner) do. Returns a short status text for the log.
        public static string RegisterSpawned(GameObject go, string baseName)
        {
            try
            {
                GameObject counter = GameRefs.ItemNameId;
                FsmInt n = null;
                if (counter != null)
                {
                    PlayMakerFSM[] fsms = counter.GetComponents<PlayMakerFSM>();
                    for (int i = 0; i < fsms.Length && n == null; i++)
                        if (fsms[i].FsmName == "itemNameID") n = fsms[i].FsmVariables.GetFsmInt("intName");
                }
                if (n == null) return "itemNameID counter not found";

                System.Collections.ArrayList list = SavedItemList();
                if (list == null) return "ArrayList_Items not found on NewGO_ArrayList";

                go.name = baseName + "(Clone)" + n.Value;
                n.Value = n.Value + 1;
                list.Add(go);
                InvalidateLiveNames();
                return "registered as " + go.name;
            }
            catch (Exception e) { return "registration failed: " + e.Message; }
        }

        // The game's list of things it saves (ArrayList_Items on NewGO_ArrayList). Looked up fresh each time (this is
        // rare), because the game may give the list a new instance when a save is loaded.
        static System.Collections.ArrayList SavedItemList()
        {
            GameObject reg = GameRefs.NewGOArrayList;
            if (reg == null) return null;
            foreach (Component c in reg.GetComponents<Component>())
            {
                if (c == null || c.GetType().Name != "PlayMakerArrayListProxy") continue;
                Type t = c.GetType();
                FieldInfo rn = t.GetField("referenceName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (rn == null || (rn.GetValue(c) as string) != "ArrayList_Items") continue;
                System.Collections.ArrayList list = null;
                foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    if (f.FieldType == typeof(System.Collections.ArrayList)) { list = f.GetValue(c) as System.Collections.ArrayList; if (list != null) break; }
                if (list == null)
                {
                    PropertyInfo p = t.GetProperty("arrayList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null) list = p.GetValue(c, null) as System.Collections.ArrayList;
                }
                return list;
            }
            return null;
        }

        // ---- spawning items (delivery goods) ----------------------------------------------
        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

        // An item prefab by name (e.g. dogfood_can): the loaded asset, not an object in the world.
        public static GameObject FindPrefab(string name)
        {
            string want = Util.Norm(name.TrimStart('='));
            GameObject g;
            if (prefabs.TryGetValue(want, out g) && g != null) return g;
            foreach (GameObject go in QuestWorld.Objects)
            {
                if (go == null || go.scene.IsValid() || go.transform.parent != null) continue;
                if (Util.Norm(go.name) != want) continue;
                bool item = false;
                foreach (PlayMakerFSM f in go.GetComponents<PlayMakerFSM>()) if (f != null && f.FsmName == "ItemName") { item = true; break; }
                if (!item) continue;
                prefabs[want] = go;
                return go;
            }
            return null;
        }

        // Finds a spot on the ground level with the giver's feet, in front of them. Looks only a little above the feet so a roof,
        // tank or truck body next to the giver is never picked, and tries other directions when the spot is blocked.
        static Vector3 GroundNear(Vector3 feet, Vector3 fwd, Vector3 side)
        {
            float[] turns = { 0f, 40f, -40f, 80f, -80f, 130f, -130f, 180f };
            for (int t = 0; t < turns.Length; t++)
            {
                Vector3 dir = Quaternion.Euler(0f, turns[t], 0f) * fwd;
                Vector3 p = feet + dir * 1.4f + side;
                RaycastHit[] hits = QuestWorld.RaycastAll(new Vector3(p.x, feet.y + 0.8f, p.z), Vector3.down, 2.5f, ~0, QueryTriggerInteraction.Ignore);
                Array.Sort(hits, delegate (RaycastHit a, RaycastHit b) { return a.distance.CompareTo(b.distance); });
                for (int k = 0; k < hits.Length; k++)
                {
                    Collider c = hits[k].collider;
                    if (c == null || hits[k].distance <= 0.001f) continue;
                    if (c.GetComponentInParent<PlayMakerFSM>() != null && c.attachedRigidbody != null) continue;   // a person or an item, not ground
                    if (hits[k].point.y > feet.y + 0.5f) break;
                    Vector3 g = hits[k].point;
                    if (!Physics.CheckSphere(g + Vector3.up * 0.45f, 0.2f, ~0, QueryTriggerInteraction.Ignore)) return g;
                    break;
                }
            }
            return feet + fwd * 0.6f;
        }

        // Goods that were just handed out are watched for a few seconds: one that falls through the ground, is deleted by another
        // mod, or is moved far away without the player carrying it is put back at its spot.
        class Watched { public GameObject Go; public Vector3 Home; public GameObject Prefab; public string BaseName; public float Until; public Action<string> Log; public int Fixes; }
        static readonly List<Watched> watched = new List<Watched>();
        static float nextWatch;

        public static void WatchSpawns()
        {
            if (watched.Count == 0 || Time.time < nextWatch) return;
            nextWatch = Time.time + 0.4f;
            for (int i = watched.Count - 1; i >= 0; i--)
            {
                Watched w = watched[i];
                if (Time.time > w.Until || w.Fixes >= 3) { watched.RemoveAt(i); continue; }
                try
                {
                    bool lost = w.Go == null;
                    if (!lost && w.Go.transform.parent == null)
                    {
                        Vector3 pos = w.Go.transform.position;
                        if (pos.y < w.Home.y - 2.5f) lost = true;                                             // fell through the world
                        else if (new Vector2(pos.x - w.Home.x, pos.z - w.Home.z).magnitude > 40f) lost = true;   // thrown far away
                    }
                    if (!lost) continue;
                    if (w.Go != null) UnityEngine.Object.Destroy(w.Go);
                    w.Fixes++;
                    w.Go = UnityEngine.Object.Instantiate(w.Prefab, w.Home + Vector3.up * 0.2f, Quaternion.identity);
                    w.Go.SetActive(true);
                    w.Log("Delivery: " + w.BaseName + " was lost; put back (" + RegisterSpawned(w.Go, w.BaseName) + ").");
                }
                catch (Exception) { watched.RemoveAt(i); }
            }
        }

        // Puts `count` copies of an item on the ground in front of `who`, registered in the game's save like the
        // game's own spawns. Returns how many were made.
        public static int SpawnItemsInFront(string name, int count, Transform who, Action<string> log)
        {
            GameObject prefab = FindPrefab(name);
            if (prefab == null || who == null) { log("Delivery: no prefab named " + name); return 0; }
            Vector3 fwd = who.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            string baseName = BaseName(prefab.name);
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = GroundNear(who.position, fwd, right * ((i - (count - 1) * 0.5f) * 0.5f));
                GameObject go = UnityEngine.Object.Instantiate(prefab, p, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                go.SetActive(true);
                log("Delivery: spawned " + baseName + ", " + RegisterSpawned(go, baseName));
                watched.Add(new Watched { Go = go, Home = p, Prefab = prefab, BaseName = baseName, Until = Time.time + 12f, Log = log });
                made++;
            }
            return made;
        }

        // Turns the game's own hostility switch on or off for one NPC. Returns true when it changed something.
        public static bool SetPlayerEnemy(GameObject go, bool enemy)
        {
            if (go == null) return false;
            foreach (PlayMakerFSM f in go.GetComponents<PlayMakerFSM>())
            {
                if (f == null || f.FsmName != "PlayerIsEnemy") continue;
                string want = enemy ? "PlayerEnemy" : "PlayerFriendly";
                if (f.ActiveStateName == want) return false;
                f.Fsm.SetState(want);
                return true;
            }
            return false;
        }

        // The game's own "is this one hostile to the player?" switch: FSM PlayerIsEnemy, state PlayerEnemy or PlayerFriendly.
        public static bool IsPlayerEnemy(GameObject go)
        {
            if (go == null) return false;
            PlayMakerFSM[] fsms = go.GetComponents<PlayMakerFSM>();
            for (int i = 0; i < fsms.Length; i++)
                if (fsms[i] != null && fsms[i].FsmName == "PlayerIsEnemy") return fsms[i].ActiveStateName == "PlayerEnemy";
            return false;
        }

        // Ground height below a world x/z; false when no collider is loaded there.
        public static bool GroundAt(float x, float z, out Vector3 hit)
        {
            RaycastHit h;
            if (Physics.Raycast(new Vector3(x, 1500f, z), Vector3.down, out h, 4000f, ~0, QueryTriggerInteraction.Ignore))
            {
                hit = h.point;
                return true;
            }
            hit = Vector3.zero;
            return false;
        }

        // Camps, caves, buildings and wrecks in the loaded MapMagic tiles whose horizontal distance
        // from `center` is between minD and maxD. kindFilter ("" = any) is a word like "cave".
        public static List<Poi> FindPois(Vector3 center, float minD, float maxD, string kindFilter)
        {
            var list = new List<Poi>(); string filter = Util.Norm(kindFilter);
            foreach (var p in QuestWorld.Pois)
            {
                if (p.Obj == null || !p.Obj.gameObject.activeInHierarchy) continue;
                p.Pos = p.Obj.position;
                if (filter.Length > 0 && !Util.Norm(p.Name).Contains(filter) && !Util.Norm(p.Kind).Contains(filter)) continue;
                float d = HDist(p.Pos, center); if (d >= minD && d <= maxD) list.Add(p);
            }
            return list;
        }

        // Living enemies (objects whose Health FSM is still in its "health" state) within radius of center.
        public static List<EnemyInfo> FindEnemies(Vector3 center, float radius)
        {
            var list = new List<EnemyInfo>();
            var all = QuestWorld.People;
            if (all == null) return list;
            for (int i = 0; i < all.Count; i++)
            {
                PlayMakerFSM f = all[i];
                if (f == null || f.FsmName != "Health") continue;
                GameObject go = f.gameObject;
                if (go == null || !go.activeInHierarchy) continue;
                string name = BaseName(go.name);
                if (name.StartsWith("Player", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_Dead", StringComparison.OrdinalIgnoreCase)) continue;
                if (f.ActiveStateName != "health") continue;
                if (HDist(go.transform.position, center) > radius) continue;
                var e = new EnemyInfo();
                e.Name = name;
                e.Tag = go.tag;
                e.Pos = go.transform.position;
                e.Go = go;
                list.Add(e);
            }
            return list;
        }

        // Loaded NPCs and enemies (anything with a Health FSM) by object name, rebuilt at most once a second and shared
        // by everything that asks "is this NPC here?", so the big PlayMaker FSM list is not walked again for every question.
        static readonly Dictionary<string, Transform> liveNames = new Dictionary<string, Transform>();
        static float liveNamesAt = -10f;

        public static Dictionary<string, Transform> LiveNames()
        {
            float now = Time.unscaledTime;
            if (now - liveNamesAt < 1f && now >= liveNamesAt) return liveNames;
            liveNamesAt = now;
            liveNames.Clear();
            var all = QuestWorld.People;
            if (all == null) return liveNames;
            for (int i = 0; i < all.Count; i++)
            {
                PlayMakerFSM f = all[i];
                if (f == null || f.FsmName != "Health") continue;
                GameObject go = f.gameObject;
                if (go == null) continue;
                string n = go.name;
                if (!liveNames.ContainsKey(n)) liveNames[n] = go.transform;
            }
            return liveNames;
        }

        // The Transform of a loaded NPC by object name, or null.
        public static Transform FindLive(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Transform t;
            return LiveNames().TryGetValue(name, out t) && t != null ? t : null;
        }

        // Forget the shared list (after something was spawned or renamed), so the next question sees the change.
        public static void InvalidateLiveNames() { liveNamesAt = -10f; }

        // Everything alive and named like one of the keywords (NPCs and people-type enemies), found live through the
        // PlayMaker FSM list so NPCs that spawn later are picked up the next time this is called.
        public static List<EnemyInfo> FindPeople(string[] keywords)
        {
            var list = new List<EnemyInfo>();
            var all = QuestWorld.People;
            if (all == null || keywords == null) return list;
            var seen = new HashSet<int>();
            for (int i = 0; i < all.Count; i++)
            {
                PlayMakerFSM f = all[i];
                if (f == null) continue;
                GameObject go = f.gameObject;
                if (go == null || !go.activeInHierarchy) continue;
                string name = BaseName(go.name);
                if (name.EndsWith("_Dead", StringComparison.OrdinalIgnoreCase)) continue;
                string n = Util.Norm(name);
                bool match = false;
                for (int k = 0; k < keywords.Length && !match; k++) if (keywords[k].Length > 0 && n.Contains(keywords[k])) match = true;
                if (!match || !seen.Add(go.GetInstanceID())) continue;
                var e = new EnemyInfo();
                e.Name = name;
                e.Tag = go.tag;
                e.Pos = go.transform.position;
                e.Go = go;
                list.Add(e);
            }
            return list;
        }

        // ---- bodies of killed enemies ---------------------------------------------------------------
        // A dead enemy leaves a "<Name>_Dead" object (an item with an ID FSM, saved with the game) that can be carried.
        // Bodies within radius of center, nearest first.
        public static List<GameObject> FindBodies(Vector3 center, float radius)
        {
            var list = new List<GameObject>();
            var all = QuestWorld.People;
            if (all == null) return list;
            var seen = new HashSet<int>();
            for (int i = 0; i < all.Count; i++)
            {
                PlayMakerFSM f = all[i];
                if (f == null || f.FsmName != "ID") continue;
                GameObject go = f.gameObject;
                if (go == null || !BaseName(go.name).EndsWith("_Dead", StringComparison.OrdinalIgnoreCase)) continue;
                if (HDist(go.transform.position, center) > radius) continue;
                if (seen.Add(go.GetInstanceID())) list.Add(go);
            }
            list.Sort(delegate (GameObject a, GameObject b) { return HDist(a.transform.position, center).CompareTo(HDist(b.transform.position, center)); });
            return list;
        }

        // ---- places the player has been to ----------------------------------------------------------
        // World positions of the map markers that the game has turned yellow = places the player has visited (the game's
        // Visited FSM does this; Immersive Map reads it the same way). Found by type name so the TextMeshPro DLL is not needed.

        public static List<Vector3> VisitedIcons()
        {
            var result = new List<Vector3>();
            foreach (var icon in QuestWorld.Icons) if (icon != null) { var c = icon.color; if (c.r > .8f && c.g > .8f && c.b < .5f) result.Add(icon.transform.position); }
            return result;
        }

        public static bool NearAny(List<Vector3> points, Vector3 p, float radius)
        {
            for (int i = 0; i < points.Count; i++) if (HDist(points[i], p) <= radius) return true;
            return false;
        }

        // ---- money ("nuts") ---------------------------------------------------------

        static FsmInt money;
        static string moneyKey = "\u0001";
        static bool listedGlobals;

        // configured = exact global variable name, or "" to detect it.
        public static FsmInt MoneyVar(string configured)
        {
            if (configured == null) configured = "";
            if (money != null && moneyKey == configured) return money;

            money = null;
            moneyKey = configured;
            PlayMakerGlobals g = PlayMakerGlobals.Instance;
            if (g == null || g.Variables == null || g.Variables.IntVariables == null) return null;
            FsmInt[] ints = g.Variables.IntVariables;

            if (!listedGlobals)
            {
                listedGlobals = true;
                var names = new List<string>();
                for (int i = 0; i < ints.Length; i++) if (ints[i] != null) names.Add(ints[i].Name + "=" + ints[i].Value);
                Plugin.Log.LogInfo("PlayMaker global ints: " + string.Join(", ", names.ToArray()));
            }

            if (configured.Length > 0)
            {
                for (int i = 0; i < ints.Length; i++)
                    if (ints[i] != null && string.Equals(ints[i].Name, configured, StringComparison.OrdinalIgnoreCase)) { money = ints[i]; break; }
                if (money == null) Plugin.Log.LogWarning("MoneyVariable '" + configured + "' is not a PlayMaker global int.");
                return money;
            }

            string[] exact = { "nuts", "money", "cash", "coins", "currency" };
            for (int e = 0; e < exact.Length && money == null; e++)
                for (int i = 0; i < ints.Length; i++)
                    if (ints[i] != null && string.Equals(ints[i].Name, exact[e], StringComparison.OrdinalIgnoreCase)) { money = ints[i]; break; }
            if (money == null)
            {
                string[] part = { "nut", "money", "cash", "coin" };
                for (int e = 0; e < part.Length && money == null; e++)
                    for (int i = 0; i < ints.Length; i++)
                        if (ints[i] != null && ints[i].Name != null && ints[i].Name.ToLowerInvariant().Contains(part[e])) { money = ints[i]; break; }
            }
            if (money != null) Plugin.Log.LogInfo("Using money variable: " + money.Name);
            else Plugin.Log.LogWarning("Could not find the money variable among the PlayMaker globals (see the list above). Set MoneyVariable in the config.");
            return money;
        }

        // The nuts you hold; -1 when the money variable can't be found.
        public static int GetMoney(string configured)
        {
            if (PartyQuests.RemotePeer != 0) return MultiplayerParty.Balance(PartyQuests.RemotePeer, "g.Money");
            FsmInt m = MoneyVar(configured);
            return m == null ? -1 : m.Value;
        }

        public static bool AddMoney(int amount, string configured)
        {
            if (PartyQuests.RemotePeer != 0) return MultiplayerParty.Adjust(PartyQuests.RemotePeer, "g.Money", amount);
            FsmInt m = MoneyVar(configured);
            if (m == null) return false;
            m.Value = m.Value + amount;
            return true;
        }

        internal static bool AddPartyMoney(int amount, string configured)
        {
            if (Multiplayer.Client) return false;
            var money = MoneyVar(configured); if (money == null || (long)money.Value + amount > int.MaxValue) return false;
            money.Value += amount;
            MultiplayerParty.Award(amount); return true;
        }

        // ---- items ------------------------------------------------------------------

        static bool UnderPlayer(Transform t)
        {
            for (int i = 0; t != null && i < 16; i++, t = t.parent)
            {
                string n = t.name;
                if (n == "PlayerCamera" || n == "PlayerCameraHolder" || n == "Player") return true;
            }
            return false;
        }

        public static FsmInt FindInt(PlayMakerFSM f, string name)
        {
            if (f == null || f.FsmVariables == null) return null;
            FsmInt[] a = f.FsmVariables.IntVariables;
            if (a == null) return null;
            for (int i = 0; i < a.Length; i++) if (a[i] != null && a[i].Name == name) return a[i];
            return null;
        }

        public static FsmFloat FindFloat(PlayMakerFSM f, string name)
        {
            if (f == null || f.FsmVariables == null) return null;
            FsmFloat[] a = f.FsmVariables.FloatVariables;
            if (a == null) return null;
            for (int i = 0; i < a.Length; i++) if (a[i] != null && a[i].Name == name) return a[i];
            return null;
        }

        // Containers (water cans, fuel cans) have a LiquidAmount FSM with Liquid and LiquidCapacity. An item that
        // holds a liquid only counts when it is at least `minFraction` full, so an empty can is not a water can.
        public static float MinLiquidFraction = 0.5f;
        static bool TooEmpty(GameObject go)
        {
            if (MinLiquidFraction <= 0f) return false;
            PlayMakerFSM[] fsms = go.GetComponents<PlayMakerFSM>();
            for (int i = 0; i < fsms.Length; i++)
            {
                if (fsms[i] == null || fsms[i].FsmName != "LiquidAmount") continue;
                FsmFloat liq = FindFloat(fsms[i], "Liquid");
                FsmFloat cap = FindFloat(fsms[i], "LiquidCapacity");
                if (liq == null) return false;
                float full = cap != null && cap.Value > 0f ? cap.Value : 0f;
                if (full <= 0f) return liq.Value <= 0.01f;
                return liq.Value < full * MinLiquidFraction;
            }
            return false;
        }

        // Capacity (litres) of the first loaded container matching the item token, or 0 if it is not a container
        // or none is loaded. Also gives the fullest matching container's current amount.
        public static float ContainerCapacity(string token, out float bestHave)
        {
            bestHave = 0f;
            float cap = 0f;
            var all = QuestWorld.Items;
            if (all == null) return 0f;
            for (int i = 0; i < all.Count; i++)
            {
                PlayMakerFSM f = all[i];
                if (f == null || f.FsmName != "ItemName") continue;
                GameObject go = f.gameObject;
                if (go == null) continue;
                string display;
                if (!Matches(go, f, token, out display) || ForSale(go)) continue;
                PlayMakerFSM[] fsms = go.GetComponents<PlayMakerFSM>();
                for (int k = 0; k < fsms.Length; k++)
                {
                    if (fsms[k] == null || fsms[k].FsmName != "LiquidAmount") continue;
                    FsmFloat c = FindFloat(fsms[k], "LiquidCapacity");
                    FsmFloat l = FindFloat(fsms[k], "Liquid");
                    if (c != null && c.Value > cap) cap = c.Value;
                    if (l != null && UnderPlayer(go.transform) && l.Value > bestHave) bestHave = l.Value;
                }
            }
            return cap;
        }

        // In-game clock in hours since day 0 (Day * 24 + hour of day), from the game manager's DayTime FSM.
        // Returns -1 when the clock cannot be found. Sleeping and time skips count; pausing does not.
        static PlayMakerFSM dayFsm;
        public static double GameHours()
        {
            if (dayFsm == null) dayFsm = FsmUtil.Find(GameRefs.GameManager, "DayTime");
            if (dayFsm == null)
            {
                var all = QuestWorld.All;
                if (all == null) return -1.0;
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && all[i].FsmName == "DayTime") { dayFsm = all[i]; break; }
                if (dayFsm == null) return -1.0;
            }
            FsmInt day = FindInt(dayFsm, "Day");
            FsmFloat time = FindFloat(dayFsm, "Time");
            if (day == null || time == null) return -1.0;
            return day.Value * 24.0 + time.Value;
        }

        public static string FindString(PlayMakerFSM f, string name)
        {
            if (f == null || f.FsmVariables == null) return "";
            FsmString[] a = f.FsmVariables.StringVariables;
            if (a == null) return "";
            for (int i = 0; i < a.Length; i++) if (a[i] != null && a[i].Name == name) return a[i].Value ?? "";
            return "";
        }

        public static string BaseName(string goName)
        {
            if (string.IsNullOrEmpty(goName)) return "";
            string s = goName;
            int i = s.IndexOf("(Clone)", StringComparison.Ordinal);
            if (i >= 0) s = s.Substring(0, i);
            return s.Trim();
        }

        static bool ForSale(GameObject go)
        {
            PlayMakerFSM[] fsms = go.GetComponents<PlayMakerFSM>();
            for (int i = 0; i < fsms.Length; i++)
                if (fsms[i] != null && fsms[i].FsmName == "Buyable" && fsms[i].enabled) return true;
            return false;
        }

        // token matches the prefab name, the item's display name or its ID category (case/space/underscore-insensitive).
        // A trailing * means "starts with". A leading = means the prefab name only (alcohol cans have the ID dogfood_can).
        static bool MatchUncached(GameObject go, PlayMakerFSM itemNameFsm, string token, out string display)
        {
            display = Game.FindString(itemNameFsm, "ItemName");
            string t = Util.Norm(token);
            bool exact = false, prefix = false;
            if (t.StartsWith("=")) { exact = true; t = t.Substring(1); }     // "=prefab": that prefab only, no ID category
            if (t.EndsWith("*")) { prefix = true; t = t.Substring(0, t.Length - 1); }
            if (t.Length == 0) return false;

            string b = Util.Norm(BaseName(go.name));
            if (Eq(b, t, prefix)) return true;
            if (exact) return false;
            string d = Util.Norm(display);
            if (Eq(d, t, prefix)) return true;

            PlayMakerFSM[] fsms = go.GetComponents<PlayMakerFSM>();
            for (int i = 0; i < fsms.Length; i++)
            {
                if (fsms[i] == null || fsms[i].FsmName != "ID") continue;
                string id = Util.Norm(FindString(fsms[i], "ID"));
                if (Eq(id, t, prefix)) return true;
            }
            return false;
        }

        static bool Eq(string a, string t, bool prefix)
        {
            return prefix ? a.StartsWith(t, StringComparison.Ordinal) : a == t;
        }

        // The player's spare ammo lives as plain counters on the game manager's "Ammo" FSM (ammo_9mm, ammo_556 ...),
        // not as objects: picking a box up empties it into the counter. A quest that asks for ammo boxes can be paid
        // from that counter, one box = the usual box size.
        static readonly string[][] AmmoTypes = new string[][]
        {
            // prefab,           counter,        rounds per box
            new[] { "ammo_box_9mm",     "ammo_9mm",      "14" },
            new[] { "ammo_box_22",      "ammo_22caliber","13" },
            new[] { "ammo_box_12gauge", "ammo_12gauge",  "9"  },
            new[] { "ammo_box_20gauge", "ammo_20gauge",  "9"  },
            new[] { "ammo_box_556mm",   "ammo_556",      "14" },
            new[] { "ammo_box_762mm",   "ammo_762",      "9"  },
            new[] { "ammo_box_3006",    "ammo_3006",     "14" },
            new[] { "ammo_arrow",       "ammo_arrow",    "5"  },
        };
        static PlayMakerFSM ammoFsm;

        static PlayMakerFSM AmmoFsm()
        {
            if (ammoFsm != null) return ammoFsm;
            var all = QuestWorld.All;
            if (all == null) return null;
            for (int i = 0; i < all.Count; i++)
            {
                PlayMakerFSM f = all[i];
                if (f == null || f.FsmName != "Ammo" || f.gameObject == null) continue;
                if (f.gameObject.name == "__GameManager__" && FindInt(f, "ammo_9mm") != null) { ammoFsm = f; break; }
            }
            return ammoFsm;
        }

        static void AddReserveAmmo(string token, List<ItemHit> hits)
        {
            string t = Util.Norm(token);
            if (t.StartsWith("=")) t = t.Substring(1);
            bool prefix = t.EndsWith("*");
            if (prefix) t = t.Substring(0, t.Length - 1);
            if (t.Length == 0) return;
            PlayMakerFSM fsm = null;
            for (int i = 0; i < AmmoTypes.Length; i++)
            {
                string prefab = Util.Norm(AmmoTypes[i][0]);
                bool hit = t == "ammo" || (prefix ? prefab.StartsWith(t, StringComparison.Ordinal) : prefab == t);
                if (!hit) continue;
                if (fsm == null) fsm = AmmoFsm();
                if (fsm == null) return;
                FsmInt counter = PartyQuests.RemotePeer != 0
                    ? new FsmInt { Name = AmmoTypes[i][1], Value = Math.Max(0, MultiplayerParty.Balance(PartyQuests.RemotePeer, "a." + AmmoTypes[i][1])) }
                    : FindInt(fsm, AmmoTypes[i][1]);
                int per = int.Parse(AmmoTypes[i][2]);
                if (counter == null || counter.Value < per) continue;
                var h = new ItemHit();
                h.Reserve = true; h.Held = true; h.Dist = 0f;
                h.Qty = counter; h.PerBox = per; h.Stack = counter.Value / per;
                h.Display = "ammo";
                hits.Add(h);
            }
        }

        // Items that are on the ground within radius of the player, or carried by the player. Items that are
        // currently for sale at a merchant are ignored.
        public static List<ItemHit> FindItems(string token, Vector3 playerPos, float radius, bool countCarried)
        {
            var hits = new List<ItemHit>();
            var all = QuestWorld.Items;
            if (all == null) return hits;

            for (int i = 0; i < all.Count; i++)
            {
                PlayMakerFSM f = all[i];
                if (f == null || f.FsmName != "ItemName") continue;
                GameObject go = f.gameObject;
                if (go == null) continue;

                // cheap distance test first: carried items move with the player, so anything far away is neither carried
                // nor close enough, and the slower "is it under the player?" walk is only done for nearby items
                float dist = HDist(go.transform.position, playerPos);
                if (radius <= 100f && dist > radius && dist > 30f) continue;
                string display;
                if (!Matches(go, f, token, out display)) continue;      // with a big radius most items reach this point: match before the slower walk
                bool held = UnderPlayer(go.transform);
                if (held) { if (!countCarried) continue; }
                else if (dist > radius) continue;
                if (ForSale(go)) continue;
                if (TooEmpty(go)) continue;

                var h = new ItemHit();
                h.Go = go;
                h.Held = held;
                h.Dist = dist;
                h.Display = display;
                PlayMakerFSM[] fsms = go.GetComponents<PlayMakerFSM>();
                for (int k = 0; k < fsms.Length; k++)
                {
                    if (fsms[k] != null && fsms[k].FsmName == "Quantity")
                    {
                        FsmInt q = FindInt(fsms[k], "Quantity");
                        if (q != null) { h.Qty = q; h.Stack = Math.Max(0, q.Value); }
                        break;
                    }
                }
                if (h.Stack > 0) hits.Add(h);
            }
            if (countCarried) AddReserveAmmo(token, hits);
            return hits;
        }

        public static int CountStacks(List<ItemHit> hits)
        {
            int n = 0;
            for (int i = 0; i < hits.Count; i++) n += hits[i].Stack;
            return n;
        }

        // Removes `count` items: ground items first, then carried ones. Ground items are taken nearest first, measured from
        // `from` when given (the person you are handing them to: the thing you put down in front of them goes before
        // something that just happens to lie nearer to you), otherwise from where you stood when the items were counted.
        public static void TakeItems(List<ItemHit> hits, int count, Vector3? from = null)
        {
            if (from.HasValue)
                for (int i = 0; i < hits.Count; i++)
                    if (hits[i].Go != null && !hits[i].Held) hits[i].Dist = HDist(hits[i].Go.transform.position, from.Value);
            hits.Sort(delegate (ItemHit a, ItemHit b)
            {
                if (a.Held != b.Held) return a.Held ? 1 : -1;
                return a.Dist.CompareTo(b.Dist);
            });
            int remaining = count;
            for (int i = 0; i < hits.Count && remaining > 0; i++)
            {
                ItemHit h = hits[i];
                if (h.Reserve)
                {
                    int boxes = Math.Min(h.Stack, remaining);
                    if (h.Qty != null) h.Qty.Value = Math.Max(0, h.Qty.Value - boxes * h.PerBox);
                    remaining -= boxes;
                    continue;
                }
                if (h.Go == null) continue;
                int take = Math.Min(h.Stack, remaining);
                if (h.Qty != null && h.Stack > take)
                {
                    h.Qty.Value = h.Stack - take;       // partial stack: keep the rest
                }
                else
                {
                    if (h.Qty != null) h.Qty.Value = 0;
                    Plugin.Log.LogInfo("Handed in " + h.Go.name + (h.Held ? " (carried)" : " (" + h.Dist.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m away)"));
                    UnityEngine.Object.Destroy(h.Go);       // the game's DeleteDestroyedItems FSM drops it from the save list
                }
                remaining -= take;
            }
        }
        internal static void ResetWorld()
        {
            dayFsm = null; ammoFsm = null; money = null;
            liveNames.Clear(); liveNamesAt = -10; MatchesByToken.Clear(); _matches = 0; prefabs.Clear(); watched.Clear();
        }
    }
}
