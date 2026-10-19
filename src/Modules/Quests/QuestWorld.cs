using System;
using System.Collections.Generic;
using ES3Internal;
using Den.Tools;
using MapMagic.Products;
using MapMagic.Terrains;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using WildWasteland.Core;

namespace WildWasteland.Modules.Quests
{
    internal static class QuestWorld
    {
        internal static readonly List<PlayMakerFSM> All = new List<PlayMakerFSM>(), Items = new List<PlayMakerFSM>(), People = new List<PlayMakerFSM>();
        internal static readonly List<GameObject> Objects = new List<GameObject>();
        internal static readonly List<Poi> Pois = new List<Poi>();
        internal static readonly List<TextMeshPro> Icons = new List<TextMeshPro>();
        static readonly List<PlayMakerFSM> Next = new List<PlayMakerFSM>(), NextItems = new List<PlayMakerFSM>(), NextPeople = new List<PlayMakerFSM>();
        static readonly List<PlayMakerFSM> ClientPeople = new List<PlayMakerFSM>();
        static readonly Stack<Transform> Walk = new Stack<Transform>();
        static readonly Dictionary<int, GameObject> Known = new Dictionary<int, GameObject>();
        static readonly List<int> ObjectIds = new List<int>();
        static readonly List<GameObject> Roots = new List<GameObject>();
        static int _scan, _prefab, _sweep; static float _at; static ES3ReferenceMgrBase _manager;
        internal static int Revision;
        internal static bool Ready;
        sealed class FsmSet { internal GameObject Go; internal PlayMakerFSM[] Value; internal float Until; }
        static readonly Dictionary<int, FsmSet> Sets = new Dictionary<int, FsmSet>();
        static RaycastHit[] _rays = new RaycastHit[64];
        static readonly Dictionary<int, RaycastHit[]> ExactRays = new Dictionary<int, RaycastHit[]>();

        internal static void Begin()
        {
            Clear();
            Multiplayer.AppearanceRequested += ClientSpawned;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i); if (!scene.isLoaded) continue;
                Roots.Clear(); scene.GetRootGameObjects(Roots);
                foreach (var root in Roots) Walk.Push(root.transform);
            }
            TerrainTile.OnTileApplied -= Applied; TerrainTile.OnTileApplied += Applied;
        }
        static void Applied(TerrainTile tile, TileData data, StopToken stop)
        { if (tile != null) Walk.Push(tile.transform); }
        internal static void Clear()
        {
            Multiplayer.AppearanceRequested -= ClientSpawned; ClientPeople.Clear();
            TerrainTile.OnTileApplied -= Applied;
            All.Clear(); Items.Clear(); People.Clear(); Next.Clear(); NextItems.Clear(); NextPeople.Clear(); Objects.Clear(); Pois.Clear(); Icons.Clear();
            Known.Clear(); ObjectIds.Clear(); Walk.Clear(); Sets.Clear(); Roots.Clear(); _scan = _prefab = _sweep = Revision = 0; _at = 0; _manager = null; Ready = false;
        }
        static void ClientSpawned(GameObject go, string prefab)
        {
            // Guest NPC FSMs stay disabled and therefore never enter PlayMakerFSM.FsmList.
            if (!Multiplayer.Client || go == null || Multiplayer.CreatureObject(go) != go) return;
            var health = FsmUtil.Find(go, "Health");
            if (health != null && !ClientPeople.Contains(health)) ClientPeople.Add(health);
        }
        static void Add(GameObject go)
        {
            if (go == null) return;
            int id = go.GetInstanceID(); GameObject previous;
            if (Known.TryGetValue(id, out previous) && ReferenceEquals(previous, go)) return;
            Known[id] = go; Objects.Add(go); ObjectIds.Add(id);
        }
        internal static bool Step()
        {
            if (!(Multiplayer.Active ? GameRefs.WorldRunning : GameRefs.InPlay)) return false;
            for (int n = 0; n < 4 && Objects.Count > 0; n++)
            {
                if (_sweep >= Objects.Count) _sweep = 0;
                if (Objects[_sweep] == null)
                {
                    GameObject recorded;
                    if (Known.TryGetValue(ObjectIds[_sweep], out recorded) && ReferenceEquals(recorded, Objects[_sweep])) Known.Remove(ObjectIds[_sweep]);
                    Objects.RemoveAt(_sweep); ObjectIds.RemoveAt(_sweep);
                }
                else _sweep++;
            }
            if (Walk.Count > 0)
            {
                var t = Walk.Pop(); if (t == null) return true;
                Add(t.gameObject);
                if (Multiplayer.Client && t.name.StartsWith("MP_", StringComparison.Ordinal)) ClientSpawned(t.gameObject, null);
                for (int i = 0; i < t.childCount; i++) Walk.Push(t.GetChild(i));
                string name = Game.BaseName(t.name), kind = null;
                if (name.StartsWith("Camp_", StringComparison.Ordinal)) kind = "Camp";
                else if (name.StartsWith("Building_", StringComparison.Ordinal)) kind = "Building";
                else if (name.StartsWith("Cave_", StringComparison.Ordinal)) kind = "Cave";
                else if (name.StartsWith("Wreck_", StringComparison.Ordinal)) kind = "Wreck";
                if (kind != null && t.GetComponent<Rigidbody>() == null && !t.name.Contains("mapIcon"))
                {
                    bool exists = false; foreach (var p in Pois) if (p.Obj == t) { exists = true; break; }
                    if (!exists) Pois.Add(new Poi { Obj = t, Name = name, Kind = kind, Pos = t.position, Faction = Game.SpawnFactionOf(t) });
                }
                if (t.name == "mapIcon") { var icon = t.GetComponent<TextMeshPro>(); if (icon != null && !Icons.Contains(icon)) Icons.Add(icon); }
            }
            if (Time.unscaledTime < _at) return Walk.Count > 0;
            var source = PlayMakerFSM.FsmList; if (source == null) return false;
            for (int end = Math.Min(source.Count, _scan + 24); _scan < end; )
            {
                var f = source[_scan++]; if (f == null) continue;
                Next.Add(f); Add(f.gameObject);
                if (f.FsmName == "ItemName") NextItems.Add(f);
                else if (f.FsmName == "Health" || f.FsmName == "ID") { NextPeople.Add(f); if (f.FsmName == "Health") CrewHistory.Observe(f.gameObject); }
            }
            if (_scan < source.Count) return true;
            if (_manager == null) _manager = ES3ReferenceMgrBase.Current;
            if (_manager != null && _prefab < _manager.prefabs.Count)
            {
                int end = Math.Min(_manager.prefabs.Count, _prefab + 24);
                for (; _prefab < end; _prefab++) { var p = _manager.prefabs[_prefab]; if (p != null) Add(p.gameObject); }
                return true;
            }
            for (int i = ClientPeople.Count - 1; i >= 0; i--)
            {
                var f = ClientPeople[i];
                if (!Multiplayer.Client || f == null || Multiplayer.CreatureObject(f.gameObject) != f.gameObject) { ClientPeople.RemoveAt(i); continue; }
                if (!NextPeople.Contains(f)) NextPeople.Add(f);
            }
            All.Clear(); All.AddRange(Next); Items.Clear(); Items.AddRange(NextItems); People.Clear(); People.AddRange(NextPeople);
            Next.Clear(); NextItems.Clear(); NextPeople.Clear(); _scan = 0; _at = Time.unscaledTime + .5f; Ready = true; Revision++;
            return Walk.Count > 0;
        }
        internal static PlayMakerFSM[] Fsms(GameObject go)
        {
            if (go == null) return new PlayMakerFSM[0];
            FsmSet set; int id = go.GetInstanceID();
            if (Sets.TryGetValue(id, out set) && set.Go == go && Time.unscaledTime < set.Until) return set.Value;
            if (Sets.Count > 128) Sets.Clear();
            var value = go.GetComponents<PlayMakerFSM>(); Sets[id] = new FsmSet { Go = go, Value = value, Until = Time.unscaledTime + 1f }; return value;
        }
        internal static RaycastHit[] RaycastAll(Vector3 start, Vector3 direction, float length)
        { return RaycastAll(start, direction, length, ~0, QueryTriggerInteraction.UseGlobal); }
        internal static RaycastHit[] RaycastAll(Vector3 start, Vector3 direction, float length, int mask, QueryTriggerInteraction triggers)
        {
            int count;
            while ((count = Physics.RaycastNonAlloc(start, direction, _rays, length, mask, triggers)) >= _rays.Length && _rays.Length < 4096) Array.Resize(ref _rays, _rays.Length * 2);
            RaycastHit[] exact;
            if (!ExactRays.TryGetValue(count, out exact)) ExactRays[count] = exact = new RaycastHit[count];
            Array.Copy(_rays, exact, count); return exact;
        }
    }
}
