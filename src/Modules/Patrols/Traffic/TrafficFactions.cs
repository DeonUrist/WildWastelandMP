using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
namespace WildWasteland.Modules.Patrols
{
    internal sealed class TrafficActor : MonoBehaviour
    {
        internal string Faction;
        internal string Battle;
        internal int Side;
        internal FsmFloat Health;
        private readonly List<KeyValuePair<Behaviour,System.Reflection.PropertyInfo>> _sensors=new List<KeyValuePair<Behaviour,System.Reflection.PropertyInfo>>();
        private bool _relationSet,_hostile;
        private float _next;
        private static readonly List<PlayMakerFSM> Fs=new List<PlayMakerFSM>();
        private void Start()
        {
            foreach(var f in GetComponents<PlayMakerFSM>())if(f.FsmName=="Health")Health=f.FsmVariables.GetFsmFloat("Health");
            foreach(var sensor in GetComponentsInChildren<Behaviour>(true))if(sensor!=null&&(sensor.GetType().Name=="RangeSensor"||sensor.GetType().Name=="RaySensor"))
            {var property=sensor.GetType().GetProperty("AllowedTags");if(property!=null&&property.CanWrite&&property.PropertyType==typeof(string[]))_sensors.Add(new KeyValuePair<Behaviour,System.Reflection.PropertyInfo>(sensor,property));}
            Relations();
        }
        private void Relations()
        {
            bool hostile=TrafficFactions.PlayerHostile(Faction);if(_relationSet&&hostile==_hostile)return;_relationSet=true;_hostile=hostile;
            var tags=Faction=="Coyotes"?new List<string>{"Scrapyard","Mutant","Carnivore"}:new List<string>{"Coyotes","Mutant","Carnivore"};if(hostile)tags.Add("Player");var values=tags.ToArray();
            foreach(var sensor in _sensors)if(sensor.Key!=null)sensor.Value.SetValue(sensor.Key,values,null);
        }
        private void OnEnable(){if(!TrafficFactions.Actors.Contains(this))TrafficFactions.Actors.Add(this);}
        private void OnDisable(){TrafficFactions.Actors.Remove(this);}
        private void Update()
        { if (!PatrolsModule.Running) return;
            if(Time.time<_next)return;_next=Time.time+0.6f;Relations();if(transform.parent!=null)return;
            if(Health==null){Fs.Clear();GetComponents(Fs);foreach(var f in Fs)if(f.FsmName=="Health")Health=f.FsmVariables.GetFsmFloat("Health");Fs.Clear();}
            if(Health!=null&&Health.Value<=0f)return;
            SimpleBanditBridge.Target(gameObject,TrafficFactions.FootTarget(Faction,transform.position));
        }
    }
    internal static class TrafficFactions
    {
        private static PlayMakerFSM _relation;private static float _findRelation;
        private static PlayMakerFSM _relationOf;private static FsmInt _relationOnOff;   // Perf: "OnOff" resolved once per relation FSM (was FindFsmInt by name per call)
        internal static readonly List<TrafficActor> Actors=new List<TrafficActor>();
        internal static string Normalize(string faction)=>string.Equals(faction,"Coyotes",StringComparison.OrdinalIgnoreCase)?"Coyotes":"Scrapyard";
        internal static bool Opposed(string a,string b)=>Normalize(a)!=Normalize(b);
        internal static bool PlayerHostile(string faction)
        {
            if(Normalize(faction)!="Coyotes")return true;
            if(_relation==null&&Time.time>=_findRelation)
            {_findRelation=Time.time+2f;var manager=Core.GameRefs.GameManager;if(manager!=null)foreach(var f in manager.GetComponents<PlayMakerFSM>())if(f.FsmName=="FactionRelation_Coyotes"){_relation=f;break;}}
            if(_relation==null||!_relation.Fsm.Initialized)return false;
            if(_relation.ActiveStateName=="PlayerEnemy")return true;
            if(!ReferenceEquals(_relationOf,_relation)||_relationOnOff==null){_relationOnOff=_relation.FsmVariables.FindFsmInt("OnOff");_relationOf=_relationOnOff!=null?_relation:null;}
            return _relationOnOff!=null&&_relationOnOff.Value>0;
        }
        internal static void Actor(GameObject npc,string faction)
        {
            if(npc==null)return;faction=Normalize(faction);npc.tag=faction;
            var actor=npc.GetComponent<TrafficActor>()??npc.AddComponent<TrafficActor>();actor.Faction=faction;
            if(npc.transform.parent==null)SimpleBanditBridge.Target(npc,FootTarget(faction,npc.transform.position));
        }
        internal static void Crew(GameObject car,PatrolMarker marker)
        {
            Actor(marker.Driver,marker.Faction);Actor(marker.Passenger,marker.Faction);
            foreach(var rider in marker.Riders)Actor(rider,marker.Faction);
            var battle=car.GetComponent<BattleCar>();if(battle!=null)foreach(var actor in car.GetComponentsInChildren<TrafficActor>(true)){actor.Battle=battle.Battle;actor.Side=battle.Side;}
        }
        internal static void Dismount(GameObject npc,GameObject car)
        {
            Actor(npc,car!=null?car.GetComponent<PatrolMarker>()?.Faction:"Scrapyard");
            var battle=car!=null?car.GetComponent<BattleCar>():null;var actor=npc!=null?npc.GetComponent<TrafficActor>():null;
            if(battle!=null&&actor!=null){actor.Battle=battle.Battle;actor.Side=battle.Side;}
        }
        // Cars (ForCar/DriveTarget): the nearest opposed actor within 350 m, else the player whenever hostile (vehicle pursuit has its own
        // detection in TrafficAgent.Goal / the pilot's give-up distance).
        internal static GameObject Target(string faction,Vector3 position)
        {
            var best=NearestOpposed(faction,position);
            if(best!=null)return best;
            return !Core.Multiplayer.Hosting&&PlayerHostile(faction)?Core.Multiplayer.NearestPlayer(position):null;
        }
        // On-foot crew / foot patrols (simple bandits): faction targets as above, but the player is only offered as a goal while hostile and
        // within FootTargetRange; the NPC module then decides whether the bandit actually knows about him (senses or fought-memory).
        internal static GameObject FootTarget(string faction,Vector3 position)
        {
            var best=NearestOpposed(faction,position);
            if(best!=null)return best;
            if(!PlayerHostile(faction))return null;
            float range=PatrolsModule.FootTargetRange!=null?PatrolsModule.FootTargetRange.Value:350f;
            return Core.Multiplayer.NearestPlayer(position,range);
        }
        private static GameObject NearestOpposed(string faction,Vector3 position)
        {
            GameObject best=null;float distance=350f*350f;
            for(int i=0;i<Actors.Count;i++)
            {
                var actor=Actors[i];
                if(actor==null||!Opposed(faction,actor.Faction)||(actor.Health!=null&&actor.Health.Value<=0f))continue;
                float d=(actor.transform.position-position).sqrMagnitude;if(d>=distance)continue;
                distance=d;best=actor.gameObject;
            }
            return best;
        }
        internal static bool DriveTarget(GameObject car,out Vector3 pos,out Vector3 vel,out GameObject targetCar)
        {return DriveTarget(car,car!=null?car.GetComponent<PatrolMarker>():null,out pos,out vel,out targetCar);}
        // marker = car's PatrolMarker, cached by the caller
        internal static bool DriveTarget(GameObject car,PatrolMarker marker,out Vector3 pos,out Vector3 vel,out GameObject targetCar)
        {
            pos=vel=Vector3.zero;targetCar=null;
            var target=marker!=null?ForCar(car,marker):null;
            if(target==null)return false;
            if(Core.Multiplayer.Hosting&&IsPlayer(target))
            {
                var sight=marker.PlayerSight;if(sight.Player!=target)return false;
                pos=sight.Position;
                if(sight.Visible||sight.Heard)
                {
                    if(Core.Multiplayer.IsRemotePlayer(target)){vel=Core.Multiplayer.RemoteVelocity(target);targetCar=Core.Multiplayer.RemoteCar(target);}
                    else{Vector3 ignored;PlayerRef.Target(out ignored,out vel,out targetCar);}
                }
                return true;
            }
            if(PlayerRef.Player!=null&&target==PlayerRef.Player.gameObject)return PlayerRef.Target(out pos,out vel,out targetCar);
            if(Core.Multiplayer.IsRemotePlayer(target)){pos=target.transform.position;vel=Core.Multiplayer.RemoteVelocity(target);targetCar=Core.Multiplayer.RemoteCar(target);return true;}
            pos=target.transform.position;var body=target.GetComponentInParent<Rigidbody>();if(body!=null){vel=body.velocity;targetCar=body.gameObject;}return true;
        }
        internal static GameObject ForCar(GameObject car)
        {
            return ForCar(car,car!=null?car.GetComponent<PatrolMarker>():null);
        }
        internal static GameObject ForCar(GameObject car,PatrolMarker marker)
        {
            if(marker==null)return null;
            if(Core.Multiplayer.Hosting)
            {
                if(Time.time>=marker.FactionCheck)
                {
                    marker.FactionCheck=Time.time+0.25f;
                    var npc=Core.Services.Npc;var driver=marker.Driver;
                    marker.PlayerSight=npc!=null&&driver!=null?npc.ObservePlayers(driver,car,car.transform,PlayerHostile(marker.Faction),PatrolsModule.PatrolMemorySeconds.Value):default(Core.NpcPlayerSight);
                    marker.FactionTarget=NearestOpposed(marker.Faction,car.transform.position)??marker.PlayerSight.Player;
                }
                return marker.FactionTarget;
            }
            if(Time.time>=marker.FactionCheck){marker.FactionCheck=Time.time+0.4f;marker.FactionTarget=Target(marker.Faction,car.transform.position);}return marker.FactionTarget;
        }
        internal static bool IsPlayer(GameObject target)
        {return target!=null&&(target==Core.GameRefs.Player||Core.Multiplayer.IsRemotePlayer(target));}
        internal static GameObject GunTarget(GameObject actor,GameObject car,Transform facing,PatrolMarker marker)
        {
            var npc=Core.Services.Npc;if(npc==null||marker==null)return null;
            var sight=npc.ObservePlayers(actor,car,facing,PlayerHostile(marker.Faction),PatrolsModule.DismountMemorySeconds.Value);
            return NearestOpposed(marker.Faction,actor.transform.position)??(sight.Visible?sight.Player:null);
        }
    }
}
