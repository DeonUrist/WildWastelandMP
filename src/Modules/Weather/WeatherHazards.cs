using System.Collections.Generic;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Weather
{
    internal sealed class WeatherHazards
    {
        private readonly Collider[] _hits=new Collider[2048];
        private readonly Dictionary<Collider,Rigidbody> _bodies=new Dictionary<Collider,Rigidbody>(4096);
        private int _forces, _players, _npcs;
        private float _report;
        private bool _announced, _overflow;
        private const int Mask=(1<<6)|(1<<8)|(1<<9)|(1<<10);

        internal void Tick(WeatherRegion region,int regionIndex,WeatherNative.Storm storm,Vector3[] columns,int count,WeatherVisuals visuals,WeatherNative native)
        {
            if (!storm.Ready || !region.Hurricane || visuals==null) { storm.Contacts(); return; }
            var rotation=Quaternion.Euler(0,region.Heading,0);
            var inverse=Quaternion.Inverse(rotation);
            Vector3 center=Origin.Scene(region.WorldCenter);
            for (int column=0;column<count && column<3;column++)
            {
                var source=storm.Explosions[column]; var force=storm.Force[column]; var damage=storm.Damage[column];
                if (source==null || force==null || damage==null || storm.Collided[column]==null) continue;
                Vector3 origin=center+rotation*columns[column];
                float ground;
                // Physics cannot depend on a camera having drawn this column (dedicated hosts have none).
                RaycastHit terrain;
                if (Physics.Raycast(origin+Vector3.up*2500,Vector3.down,out terrain,5000,1<<14,QueryTriggerInteraction.Ignore))
                    visuals.SetGround(regionIndex,column,terrain.point.y-center.y-2);
                if (!visuals.TryGround(regionIndex,column,out ground)) continue;
                origin.y+=ground;
                float width=WeatherVortexSmoke.Width(regionIndex*80+column);
                // Same 0-1150 m body, bend, eye and lobed radius as HurricaneSand. Wisps are harmless.
                int found=Physics.OverlapBoxNonAlloc(origin+Vector3.up*550,new Vector3(750*width,555,750*width),_hits,rotation,Mask,QueryTriggerInteraction.Collide);
                if (found==_hits.Length && !_overflow) { _overflow=true; Log.Warn("[Weather] Hurricane overlap buffer full (2048); excess contacts may be omitted."); }
                bool pulse=force.isActiveAndEnabled && force.ActiveStateName=="tornado";
                var previous=storm.Previous[column]; var current=storm.Current[column]; current.Clear();
                for (int i=0;i<found;i++)
                {
                    var hit=_hits[i]; _hits[i]=null;
                    if (hit==null) continue;
                    Vector3 local=inverse*(hit.bounds.center-origin);
                    Vector3 axis; float outer;
                    if (!Wall(local,regionIndex*80+column,width,out axis,out outer)) continue;
                    if (!_announced) { _announced=true; Log.Always("[Weather] Hurricane hazards active: sand wall only, safe eye; native 50 VelocityChange pulses and Bodypart damage -5 player / -5000 NPC."); }
                    var target=hit.gameObject;
                    if (target.layer==6 || target.layer==10)
                    {
                        current.Add(hit);
                        if (!previous.Contains(hit) && damage.isActiveAndEnabled && damage.ActiveStateName=="trigger")
                        {
                            storm.Collided[column].Value=target;
                            damage.SendEvent(target.layer==6 ? "next2" : "next");
                            if (target.layer==6) _players++; else _npcs++;
                        }
                    }
                    if (!pulse) continue;
                    Rigidbody body;
                    if (!_bodies.TryGetValue(hit,out body))
                    {
                        if (_bodies.Count>=4096) _bodies.Clear();
                        _bodies[hit]=body=target.GetComponent<Rigidbody>();
                    }
                    if (!native.AllowsForce(source,target)) continue;
                    // Retain native force/mode/upwards modifier and its vertically bobbing force origin.
                    // The radial falloff covers the visible body instead of the old 150 m centre sphere.
                    Vector3 blast=origin+rotation*axis;
                    blast.y=origin.y+force.transform.position.y-storm.Root.transform.position.y;
                    if (MultiplayerForces.Explosion(target,source.force.Value,blast,Mathf.Max(source.radius.Value,outer),source.upwardsModifier.Value,source.forceMode)) { _forces++; continue; }
                    if (body==null || body.isKinematic) continue;
                    body.AddExplosionForce(source.force.Value,blast,Mathf.Max(source.radius.Value,outer),source.upwardsModifier.Value,source.forceMode);
                    _forces++;
                }
                storm.Previous[column]=current; storm.Current[column]=previous; previous.Clear();
            }
            if (Log.Verbose && Time.unscaledTime>=_report)
            {
                _report=Time.unscaledTime+5;
                Log.Info("[Weather] Hazard contacts: lift applications="+_forces+", player wall entries="+_players+", NPC wall entries="+_npcs+"; native shelter filters retained.");
            }
        }
        private static bool Wall(Vector3 local,int slot,float width,out Vector3 axis,out float outer)
        {
            axis=Vector3.zero; outer=0;
            if (local.y < -5 || local.y > 1100) return false;
            float h=local.y/1600, phase=slot*1.731f;
            axis=new Vector3(Mathf.Sin(h*4+phase),0,Mathf.Cos(h*3+phase))*h*110*width;
            Vector3 radial=local-axis; radial.y=0;
            float angle=Mathf.Atan2(radial.z,radial.x);
            float radius=1450*(.44f-.14f*Mathf.Sin(Mathf.Clamp01(h)*Mathf.PI)+.76f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.62f,1,h)));
            radius*=1+.055f*Mathf.Sin(angle*3+h*5+phase)+.025f*Mathf.Cos(angle*5-h*3);
            float inner=(270+35*Mathf.Sin(h*2+phase)+100)*width;
            outer=(radius-45)*width;
            float distance=radial.sqrMagnitude;
            return distance>=inner*inner && distance<=outer*outer;
        }
        internal void Reset()
        {
            System.Array.Clear(_hits,0,_hits.Length); _bodies.Clear();
            _forces=_players=_npcs=0; _report=0; _announced=_overflow=false;
        }
    }
}
