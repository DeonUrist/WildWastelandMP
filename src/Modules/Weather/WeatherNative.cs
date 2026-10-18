using System;
using System.Collections.Generic;
using ES3Internal;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Weather
{
    // Native roots remain the encounter/save/movement anchors. Only WW draws and applies hazards.
    internal sealed class WeatherNative : IDisposable
    {
        internal const long PrefabId = 5697600806714206537L;
        internal sealed class Storm
        {
            internal GameObject Root;
            internal Rigidbody Body;
            internal bool Owned, Ready, Gravity, Selected;
            internal readonly List<Transform> Pending = new List<Transform>(64);
            internal readonly List<Visual> Visuals = new List<Visual>(64);
            internal readonly List<Switch> Switches = new List<Switch>(8);
            internal readonly PlayMakerFSM[] Force = new PlayMakerFSM[3], Damage = new PlayMakerFSM[3];
            internal readonly Explosion[] Explosions = new Explosion[3];
            internal readonly FsmGameObject[] Collided = new FsmGameObject[3];
            internal readonly HashSet<Collider>[] Previous = { new HashSet<Collider>(), new HashSet<Collider>(), new HashSet<Collider>() };
            internal readonly HashSet<Collider>[] Current = { new HashSet<Collider>(), new HashSet<Collider>(), new HashSet<Collider>() };
            internal float Age;
            internal int Id;
            internal void Contacts() { for (int i=0;i<3;i++) { Previous[i].Clear(); Current[i].Clear(); } }
        }
        internal struct Visual
        {
            internal ParticleSystem System;
            internal Renderer Renderer;
            internal AudioSource Audio;
            internal bool Playing, Paused, RenderingOff, Muted;
        }
        internal struct Switch { internal Behaviour Behaviour; internal Collider Collider; internal bool Enabled; }
        private static WeatherNative _active;
        private readonly WeatherModule _owner;
        internal readonly Storm[] Storms = new Storm[64];
        private readonly List<PlayMakerFSM> _fsms = new List<PlayMakerFSM>(8);
        private readonly List<ParticleSystem> _systems = new List<ParticleSystem>(4);
        private readonly List<Renderer> _renderers = new List<Renderer>(4);
        private readonly List<AudioSource> _audio = new List<AudioSource>(2);
        private readonly Dictionary<int,Storm> _roots = new Dictionary<int,Storm>(64);
        private readonly Dictionary<Fsm,bool> _nativeFsms = new Dictionary<Fsm,bool>(512);
        private readonly List<Fsm> _deadFsms = new List<Fsm>(512);
        private int _scan, _work;
        private bool _scanning, _saving, _restoring;
        private PlayMakerFSM _disable;
        private bool _disableEnabled;
        internal readonly Func<Explosion,GameObject,bool> AllowsForce;
        internal bool Saving { get { return _saving; } }

        internal WeatherNative(WeatherModule owner)
        {
            _owner=owner; _active=this;
            for (int i=0;i<Storms.Length;i++) Storms[i]=new Storm();
            AllowsForce=(Func<Explosion,GameObject,bool>)Delegate.CreateDelegate(typeof(Func<Explosion,GameObject,bool>),Core.Patches.Method(typeof(Explosion),"ShouldApplyForce"));
            Hooks.ExplosionDo.Prefix(owner, ExplosionBefore);
            if (!Core.Patches.Patch(owner,Core.Patches.Method(typeof(TriggerEventByLayer),"DoTriggerEnter"),prefix:Core.Patches.Method(typeof(WeatherNative),"TriggerBefore")))
                throw new InvalidOperationException("Weather requires native storm hazard interception.");
            Hooks.FsmEnable.Postfix(owner,Enabled);
            Hooks.ProcessEvent.Prefix(owner,EventBefore);
            Hooks.SetVelocityDo.Prefix(owner,VelocityBefore);
            Scheduler.Sliced(owner,"Weather.NativeDiscovery",500,Step);
            Scheduler.Interval(owner,"Weather.NativeEncounters",.5f,Reconcile);
            Scan();
            Log.Always("[Weather] Native storm replacement enabled: native encounter/save anchors retained; WW walls and safe-eye tornado hazards replace vanilla graphics/physics.");
        }
        internal void Scan() { _scan=0; _scanning=true; }
        private static GameObject Root(Transform node)
        {
            for (;node!=null;node=node.parent)
            {
                var prefab=node.GetComponent<ES3Prefab>();
                if (prefab!=null && prefab.prefabId==PrefabId && node.gameObject.scene.IsValid()) return node.gameObject;
            }
            return null;
        }
        private Storm Track(GameObject root)
        {
            if (root==null) return null;
            Storm storm;
            if (_roots.TryGetValue(root.GetInstanceID(),out storm) && storm.Root==root) return storm;
            for (int i=0;i<Storms.Length;i++)
            {
                storm=Storms[i]; if (storm.Root!=null || storm.Selected) continue;
                if (storm.Id!=0) _roots.Remove(storm.Id);
                storm.Root=root; storm.Ready=false; storm.Owned=false; storm.Selected=false; storm.Age=40;
                storm.Id=root.GetInstanceID();
                storm.Pending.Clear(); storm.Visuals.Clear(); storm.Switches.Clear(); storm.Contacts();
                Array.Clear(storm.Force,0,3); Array.Clear(storm.Damage,0,3); Array.Clear(storm.Explosions,0,3); Array.Clear(storm.Collided,0,3);
                storm.Body=root.GetComponent<Rigidbody>(); storm.Gravity=storm.Body!=null && storm.Body.useGravity;
                if (storm.Body!=null && !_saving && !Multiplayer.Client) { storm.Body.useGravity=false; var velocity=storm.Body.velocity; velocity.y=0; storm.Body.velocity=velocity; }
                var collider=root.GetComponent<Collider>();
                if (collider!=null) { storm.Switches.Add(new Switch { Collider=collider,Enabled=collider.enabled }); if (!_saving) collider.enabled=false; }
                storm.Pending.Add(root.transform); _roots[storm.Id]=storm;
                return storm;
            }
            Log.Warn("[Weather] Native storm anchor capacity (64) reached; replacement discovery deferred."); return null;
        }
        private void Enabled(PlayMakerFSM fsm)
        {
            if (_restoring || fsm==null) return;
            if (fsm.FsmName=="DisableSandstorm")
            {
                if (_disable!=fsm) { _disable=fsm; _disableEnabled=fsm.enabled; }
                fsm.enabled=false; return;
            }
            if (fsm.FsmName!="SandPlayer") return;
            Track(Root(fsm.transform));
        }
        private bool EventBefore(Fsm fsm,FsmEvent ev,FsmEventData data)
        {
            if (ev!=null && ev.Name=="SaveGame" && !_saving) Save(true);
            return true;
        }
        private static bool ExplosionBefore(Explosion __instance)
        {
            if (_active==null || __instance.Fsm.Name!="Tornado") return true;
            return !_active.NativeAction(__instance);
        }
        private static bool TriggerBefore(TriggerEventByLayer __instance)
        {
            if (_active==null || __instance.Fsm.Name!="TornadoDamage") return true;
            return !_active.NativeAction(__instance);
        }
        private bool NativeAction(FsmStateAction action)
        {
            bool native;
            if (!_nativeFsms.TryGetValue(action.Fsm,out native))
            {
                var root=Root(action.Owner.transform); native=root!=null;
                if (native) Track(root);
                _nativeFsms[action.Fsm]=native;
            }
            return native;
        }
        private bool VelocityBefore(SetVelocity action)
        {
            if (action.Fsm.Name!="Move") return true;
            Storm storm;
            if (!_roots.TryGetValue(action.Owner.GetInstanceID(),out storm)) return true;
            if (storm.Owned || WeatherDiagnostics.Frozen || !GameRefs.InGame)
            { if (storm.Body!=null && !storm.Body.isKinematic) storm.Body.velocity=Vector3.zero; return false; }
            return true;
        }
        private bool Step()
        {
            for (int k=0;k<Storms.Length;k++)
            {
                var storm=Storms[_work++%Storms.Length];
                if (storm.Root==null || storm.Pending.Count==0) continue;
                int last=storm.Pending.Count-1; var node=storm.Pending[last]; storm.Pending.RemoveAt(last);
                if (node!=null) Capture(storm,node);
                if (storm.Pending.Count==0)
                {
                    storm.Ready=true;
                    Log.Always("[Weather] Replaced native SandStorm: "+storm.Root.name+"; original particles/audio/collision hazards suppressed; native save identity retained.");
                }
                return true;
            }
            if (!_scanning) return false;
            var fsms=PlayMakerFSM.FsmList;
            if (_scan>=fsms.Count) { _scanning=false; return false; }
            Enabled(fsms[_scan++]); return true;
        }
        private void Capture(Storm storm,Transform node)
        {
            for (int i=0;i<node.childCount;i++) storm.Pending.Add(node.GetChild(i));
            node.GetComponents(_systems); node.GetComponents(_renderers); node.GetComponents(_audio); node.GetComponents(_fsms);
            for (int i=0;i<_systems.Count;i++)
            {
                var ps=_systems[i]; storm.Visuals.Add(new Visual { System=ps,Playing=ps.isPlaying,Paused=ps.isPaused });
                if (!_saving) ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            for (int i=0;i<_renderers.Count;i++)
            {
                var renderer=_renderers[i]; storm.Visuals.Add(new Visual { Renderer=renderer,RenderingOff=renderer.forceRenderingOff });
                if (!_saving) renderer.forceRenderingOff=true;
            }
            for (int i=0;i<_audio.Count;i++)
            {
                var source=_audio[i]; storm.Visuals.Add(new Visual { Audio=source,Muted=source.mute }); if (!_saving) source.mute=true;
            }
            for (int i=0;i<_fsms.Count;i++)
            {
                var fsm=_fsms[i];
                if (fsm.FsmName=="SandPlayer")
                { storm.Switches.Add(new Switch { Behaviour=fsm,Enabled=fsm.enabled }); if (!_saving) fsm.enabled=false; }
                if (fsm.FsmName!="Tornado") continue;
                int channel=node.name.EndsWith("(1)",StringComparison.Ordinal) ? 0 : node.name.EndsWith("(2)",StringComparison.Ordinal) ? 1 : 2;
                storm.Force[channel]=fsm; storm.Damage[channel]=FsmUtil.Find(node.gameObject,"TornadoDamage");
                var damage=storm.Damage[channel]; if (damage!=null) storm.Collided[channel]=damage.FsmVariables.FindFsmGameObject("collidedObj");
                if (!fsm.Fsm.Initialized) fsm.Fsm.InitStates();
                var states=fsm.FsmStates;
                for (int s=0;s<states.Length;s++)
                    for (int a=0;a<states[s].Actions.Length;a++)
                    { var explosion=states[s].Actions[a] as Explosion; if (explosion!=null) storm.Explosions[channel]=explosion; }
                var collider=node.GetComponent<Collider>();
                if (collider!=null) { storm.Switches.Add(new Switch { Collider=collider,Enabled=collider.enabled }); if (!_saving) collider.enabled=false; }
            }
        }
        internal Storm Preview(Vector3 position,Quaternion rotation)
        {
            var manager=ES3ReferenceMgrBase.Current; var prefab=manager!=null ? manager.GetPrefab(PrefabId,true) : null;
            if (prefab==null) { Log.Warn("[Weather] Native SandStorm prefab unavailable; damaging preview not spawned."); return null; }
            var root=UnityEngine.Object.Instantiate(prefab.gameObject,position,rotation);
            var storm=Track(root);
            if (storm==null) { UnityEngine.Object.Destroy(root); return null; }
            storm.Owned=true; storm.Age=0;
            var move=FsmUtil.Find(root,"Move"); if (move!=null) move.enabled=false;
            if (storm.Body!=null) { storm.Body.velocity=Vector3.zero; storm.Body.isKinematic=true; }
            return storm;
        }
        private void Reconcile()
        {
            if (Multiplayer.Client || !GameRefs.InPlay || _saving || WeatherDiagnostics.Frozen) return;
            _deadFsms.Clear();
            foreach (var pair in _nativeFsms) if (pair.Key.Owner==null) _deadFsms.Add(pair.Key);
            for (int i=0;i<_deadFsms.Count;i++) _nativeFsms.Remove(_deadFsms[i]);
            _owner.ReconcileNative();
        }
        internal void Save(bool saving)
        {
            if (_saving && saving) return;
            _saving=saving; _restoring=true;
            try { for (int i=0;i<Storms.Length;i++) if (Storms[i].Root!=null && !Storms[i].Owned) Apply(Storms[i],saving); }
            finally { _restoring=false; }
        }
        private static void Apply(Storm storm,bool restore)
        {
            if (storm.Body!=null && !Multiplayer.Client)
            {
                storm.Body.useGravity=restore && storm.Gravity;
                if (!restore) { var velocity=storm.Body.velocity; velocity.y=0; storm.Body.velocity=velocity; }
            }
            for (int i=0;i<storm.Switches.Count;i++)
            {
                var item=storm.Switches[i];
                if (item.Behaviour!=null) item.Behaviour.enabled=restore && item.Enabled;
                if (item.Collider!=null) item.Collider.enabled=restore && item.Enabled;
            }
            for (int i=0;i<storm.Visuals.Count;i++)
            {
                var item=storm.Visuals[i];
                if (item.Renderer!=null) item.Renderer.forceRenderingOff=restore ? item.RenderingOff : true;
                if (item.Audio!=null) item.Audio.mute=restore && item.Muted || !restore;
                if (item.System==null) continue;
                if (restore && (item.Playing || item.Paused)) { item.System.Play(false); if (item.Paused) item.System.Pause(false); }
                else item.System.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
        internal void Remove(Storm storm)
        {
            if (storm==null) return;
            storm.Selected=false; storm.Contacts();
            if (!storm.Owned) return;
            if (storm.Root!=null) { _roots.Remove(storm.Id); UnityEngine.Object.Destroy(storm.Root); }
            storm.Root=null; storm.Pending.Clear();
        }
        internal void Reset()
        {
            _restoring=true;
            try
            {
                for (int i=0;i<Storms.Length;i++)
                {
                    var storm=Storms[i];
                    if (storm.Root!=null) { if (storm.Owned) UnityEngine.Object.Destroy(storm.Root); else Apply(storm,true); }
                    storm.Root=null; storm.Pending.Clear(); storm.Visuals.Clear(); storm.Switches.Clear(); storm.Contacts();
                }
                if (_disable!=null) _disable.enabled=_disableEnabled;
            }
            finally { _restoring=false; }
            _disable=null; _roots.Clear(); _nativeFsms.Clear(); _deadFsms.Clear(); _saving=false; Scan();
        }
        public void Dispose() { Reset(); if (_active==this) _active=null; }
    }
}
