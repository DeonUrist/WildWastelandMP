using System.Collections.Generic;
using UnityEngine;
using ES3Internal;

namespace WildWasteland.Modules.Patrols
{
    // BuildQueue serializes assembly until Assembled; these buffers belong only to the active map spawn.
    internal static class MerchantSpawnAudit
    {
        private static readonly HashSet<GameObject> Existing=new HashSet<GameObject>(),Allowed=new HashSet<GameObject>();
        private static readonly Collider[] Hits=new Collider[512];
        private static GameObject _car;
        private static BoxCollider _area;
        internal static void Begin(GameObject car)
        {_car=car;_area=null;Existing.Clear();Allowed.Clear();Register.CaptureItems(Existing);}
        internal static void Keep(GameObject item){Allowed.Add(item);}
        internal static void CaptureArea(GameObject car)
        {
            if(!ReferenceEquals(car,_car))return;
            var node=Patrol.FindChild(car.transform,"PhysicsLock");_area=node!=null?node.GetComponent<BoxCollider>():null;
            if(_area==null){Core.Log.Warn("[Patrols] Merchant spawn audit skipped: no PhysicsLock area on "+car.name);return;}
            int count=Overlap();
            if(count==Hits.Length){System.Array.Clear(Hits,0,Hits.Length);_area=null;Core.Log.Warn("[Patrols] Merchant spawn audit skipped: initial PhysicsLock overlap buffer full on "+car.name);return;}
            for(int i=0;i<count;i++){var item=Item(Hits[i]);if(item!=null)Existing.Add(item);Hits[i]=null;}
        }
        private static int Overlap()
        {
            var t=_area.transform;var size=Vector3.Scale(_area.size,t.lossyScale)*0.5f;
            size=new Vector3(Mathf.Abs(size.x),Mathf.Abs(size.y),Mathf.Abs(size.z));
            return Physics.OverlapBoxNonAlloc(t.TransformPoint(_area.center),size,Hits,t.rotation,~0,QueryTriggerInteraction.Collide);
        }
        private static GameObject Item(Collider collider)
        {var prefab=collider!=null?collider.GetComponentInParent<ES3Prefab>():null;return prefab!=null?prefab.gameObject:null;}
        internal static int Complete(GameObject car)
        {
            if(!ReferenceEquals(car,_car)||car==null||_area==null)return 0;
            int count=Overlap(),removed=0;var host=car.GetComponent<MerchantHost>();
            if(count==Hits.Length){System.Array.Clear(Hits,0,Hits.Length);Core.Log.Warn("[Patrols] Merchant spawn audit skipped: PhysicsLock overlap buffer full on "+car.name);return 0;}
            for(int i=0;i<count;i++)
            {
                var item=Item(Hits[i]);Hits[i]=null;
                if(item==null||item==car||Existing.Contains(item)||Allowed.Contains(item)||host!=null&&host.OwnsStock(item))continue;
                var local=_area.transform.InverseTransformPoint(item.transform.position)-_area.center;var half=_area.size*0.5f;
                if(Mathf.Abs(local.x)>half.x||Mathf.Abs(local.y)>half.y||Mathf.Abs(local.z)>half.z)continue;
                if(Core.FsmUtil.Find(item,"ForBuy")==null)continue;
                Allowed.Add(item); // each item can contribute several overlapping colliders
                Core.Log.Warn("[Patrols] Removed non-trader item from merchant spawn PhysicsLock area: "+item.name);
                Register.RemoveTree(item);Core.MultiplayerItems.Consume(item);Object.Destroy(item);removed++;
            }
            return removed;
        }
        internal static void Clear(GameObject car)
        {if(!ReferenceEquals(car,_car))return;_car=null;_area=null;Existing.Clear();Allowed.Clear();System.Array.Clear(Hits,0,Hits.Length);}
    }
}
