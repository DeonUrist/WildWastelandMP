using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
namespace WildWasteland.Modules.Patrols
{
    internal static class MerchantRuntime
    {
        internal static bool StockOnly(CarTemplate template)=>template.Name=="Coyote_Trader"||template.MerchantTypes!=null&&template.MerchantTypes.Length>0;
        internal static void SpawnTemplate(Patrol patrol,CarTemplate source,SpawnGroup group)
        {if(source==null||!CrewFactions.Compatible(source,(group??MerchantCatalog.DefaultGroup()).Faction))return;group=group??MerchantCatalog.DefaultGroup();if(WorldTraffic.MerchantTypeInUse(MerchantCatalog.Selected(group.MerchantTypes))){Core.Log.Warn("[Patrols] " + "That merchant type already exists; remove its current bus before spawning another.");return;}var copy=source.CloneForSpawn();Configure(copy,group);copy.SetupOnly=true;patrol.Spawn(copy);}
        internal static void Configure(CarTemplate template,SpawnGroup group)
        {template.Faction=TrafficFactions.Normalize(group.Faction);template.MerchantGroup=group.Name;template.MerchantTypes=MerchantCatalog.Selection(group.MerchantTypes);template.SpawnLoot=new Dictionary<string,int>();template.LootPreset="car-empty";template.ReleaseHandbrake=false;template.ConvoyMember=true;}
        internal static void SpawnGroup(Patrol patrol,SpawnGroup group)
        {
            WorldTraffic.DebugDeparture(group,2);
        }
        internal static void Attach(GameObject car,CarTemplate template)
        {if(template.MerchantTypes==null||template.MerchantTypes.Length==0)return;var host=car.GetComponent<MerchantHost>()??car.AddComponent<MerchantHost>();host.Data=!string.IsNullOrEmpty(template.MerchantSaveJson)?TrafficCodec.ReadRecord<MerchantSave>(template.MerchantSaveJson):null;host.Data=host.Data??new MerchantSave{template=template.Name,group=template.MerchantGroup,types=MerchantCatalog.Selection(template.MerchantTypes),travelling=template.MerchantTravelling};host.Park();}
        internal static void Restore(GameObject car,string json)
        {
            if(string.IsNullOrEmpty(json))return;var data=TrafficCodec.ReadRecord<MerchantSave>(json);
            if(data==null||data.version!=1||data.types==null||data.types.Length>3||data.types.Any(t=>!MerchantCatalog.Known(t))||data.stock==null||data.stock.Length>36)return;
            if(data.stock.Any(s=>s==null||!data.types.Contains(s.type)||s.slot<0||s.slot>=4||string.IsNullOrEmpty(s.prefab))
                ||data.stock.GroupBy(s=>s.type+":"+s.slot).Any(g=>g.Count()>1))return;
            if(data.demands==null)data.demands=new MerchantDemand[0];
            if(data.demands.Length>3||data.demands.Any(d=>d==null||!MerchantCatalog.Known(d.type)||d.names==null||d.prices==null||d.names.Length>64||d.names.Length!=d.prices.Length))return;
            if(data.turrets!=null&&(data.turrets.Length>7||data.turrets.Any(t=>t==null||t.slot<0||t.slot>=7||float.IsNaN(t.health)||float.IsInfinity(t.health)||float.IsNaN(t.maxHealth)||float.IsInfinity(t.maxHealth))||data.turrets.GroupBy(t=>t.slot).Any(g=>g.Count()>1)))return;
            if(car.GetComponent<PatrolMarker>()==null){var marker=PatrolMarker.Attach(car,TemplateExporter.PrefabName(car.name),"",null,"",null,RamTargets.None);marker.TemplateName=data.template;marker.Faction=CarTemplate.Find(data.template)?.Faction??"Coyotes";}
            var host=car.GetComponent<MerchantHost>()??car.AddComponent<MerchantHost>();host.Data=data;host.Park();
        }
    }
    internal sealed class MerchantHost : MonoBehaviour
    {
        internal static readonly List<MerchantHost> All=new List<MerchantHost>();
        internal MerchantSave Data;
        internal bool Presentation, NetworkTrading;
        internal MerchantPose[] NetworkPoses;
        internal readonly List<MerchantObject> Objects=new List<MerchantObject>();
        private readonly List<PlayMakerFSM> _lists=new List<PlayMakerFSM>(),_sellers=new List<PlayMakerFSM>();
        private readonly Dictionary<MerchantStock,GameObject> _stock=new Dictionary<MerchantStock,GameObject>();
        internal bool OwnsStock(GameObject item){foreach(var pair in _stock)if(ReferenceEquals(pair.Value,item))return true;return false;}
        private Transform _inactiveProbes;
        private readonly List<GameObject> _probes=new List<GameObject>();
        private Transform _root;private float _next;private Collider[] _chassis;internal bool Ready;
        private PlayMakerFSM _start;
        private void OnEnable(){All.Add(this);}
        private void OnDestroy()
        {
            All.Remove(this);
            if(Presentation&&_root!=null)
            {
                foreach(var prefab in _root.GetComponentsInChildren<ES3Internal.ES3Prefab>(true))
                    if(prefab!=null&&Core.Multiplayer.ObjectId(prefab.gameObject)!=0)prefab.transform.SetParent(null,true);
                Destroy(_root.gameObject);
            }
            foreach(var pair in _stock)if(pair.Value!=null&&!pair.Key.purchased)Register.RemoveTree(pair.Value);
        }
        // Perf: marker / crew / handbrake FSM resolved once (re-resolved while missing); was GetComponent x2 + a whole-bus
        // GetComponentsInChildren<PlayMakerFSM> per Park (2/s). Park still re-asserts every call (ignition off, handbrake, inputs).
        private PatrolMarker _marker;private Crew _crew;private PlayMakerFSM _handbrake;private float _handbrakeSearch=-1f;
        internal void Park()
        {
            if(Presentation)return;
            if(_marker==null)_marker=GetComponent<PatrolMarker>();if(_crew==null)_crew=GetComponent<Crew>();
            var marker=_marker;if(marker?.PlayerEntered==true)return;
            var journey=WorldTraffic.Find(marker?.TrafficId);var crew=_crew;
            bool waiting=Data?.travelling!=true||(journey?.merchant==true&&(journey.arrived||journey.waitSeconds>0f));
            if(Data?.travelling!=true&&crew!=null)crew.Hold=true;
            if(crew!=null)crew.WaitForMerchant(waiting);if(!waiting)return;
            Nwh.StopEngine(gameObject);
            if(_start==null)_start=Patrol.FindFsm(gameObject,"START","Start");
            if(_start!=null&&_start.enabled&&_start.Fsm.Initialized&&_start.ActiveStateName!="off")_start.Fsm.SetState("off");
            if(_handbrake==null&&Time.unscaledTime>=_handbrakeSearch){_handbrakeSearch=Time.unscaledTime+5f;_handbrake=Patrol.FindFsm(gameObject,"handbrake","Handbrake");}
            Nwh.SetInput(gameObject,0f,0f,1f);Nwh.SetHandbrake(gameObject,1f);Patrol.Handbrake(gameObject,true,_handbrake);
        }
        private IEnumerator Start()
        {
            if(Data==null){Destroy(this);yield break;}Park();
            // Legacy buses could contain three shops. Keep the first selected shop and remove only
            // its discarded, unpurchased stock; purchased items remain the player's property.
            var selected=MerchantCatalog.Selected(Data.types);
            var discarded=new HashSet<string>(Data.stock.Where(s=>s.type!=selected&&!s.purchased&&!string.IsNullOrEmpty(s.nativeName)).Select(s=>s.nativeName));
            Data.types=new[]{selected};Data.stock=Data.stock.Where(s=>s.type==selected).ToArray();
            Data.demands=(Data.demands??new MerchantDemand[0]).Where(d=>d.type==selected).ToArray();
            _root=new GameObject("Apocapatrol.Merchant").transform;_root.SetParent(transform,false);
            _chassis=GetComponentsInChildren<Collider>(true);
            var stock=Data.stock.ToList();
            foreach(var type in Data.types.Distinct())
            {
                var prefab=Prefabs.FindAny(MerchantCatalog.Station(type));if(prefab==null){Core.Log.Warn("[Patrols] " + "Merchant station unavailable: "+type);continue;}
                var station=Instantiate(prefab,_root);station.name=type;station.transform.localPosition=new Vector3(0f,1.5f,-1.5f);station.transform.localRotation=Quaternion.identity;station.transform.localScale=Vector3.one*0.7f;
                foreach(var f in station.GetComponentsInChildren<PlayMakerFSM>(true))f.enabled=false;
                var list=station.GetComponents<PlayMakerFSM>().First(f=>f.FsmName=="ItemList");_lists.Add(list);
                var board=Anchor(station.transform,"Board");var box=Anchor(station.transform,"Box");
                var decor=station.transform.Find("decor");
                if(decor!=null)foreach(var child in decor.Cast<Transform>().ToArray())
                {if(child.name.StartsWith("wall_")||child.name=="fence"){Destroy(child.gameObject);continue;}child.SetParent(child.name.StartsWith("plastic_crate")?box:board,true);}
                foreach(var name in new[]{"item_list","price_list","i buy"}){var node=station.transform.Find(name);if(node!=null)node.SetParent(board,true);}
                var drop=station.transform.Find("drop here");if(drop!=null)drop.SetParent(box,true);
                var trigger=station.transform.Find("sell_trigger");if(trigger!=null)trigger.SetParent(box,true);
                var liquids=station.transform.Find("Sell_Liquids");if(liquids!=null)Add(type+"/liquids",MerchantCatalog.Label(type)+" liquid box","Box",liquids);
                Add(type+"/box",MerchantCatalog.Label(type)+" selling box","Box",box);
                Add(type+"/board",MerchantCatalog.Label(type)+" board","Board",board);
                var names=board.Find("item_list");if(names!=null)Add(type+"/names",MerchantCatalog.Label(type)+" wanted names","Names",names);
                var prices=board.Find("price_list");if(prices!=null)Add(type+"/prices",MerchantCatalog.Label(type)+" prices","Prices",prices);
                // Reparenting physical pieces must preserve the vanilla list/price lookups.
                var lookup=new GameObject("ParentLookup").transform;lookup.SetParent(station.transform,false);
                foreach(var state in list.Fsm.States)foreach(var action in state.Actions)if(action is GetChild child&&(child.childName.Value=="item_list"||child.childName.Value=="price_list"))child.gameObject=Owner(board.gameObject);
                foreach(var state in list.Fsm.States)state.Actions=state.Actions.Select(action=>action is CreateObject create?
                    (FsmStateAction)new MerchantProbeAction{source=create.gameObject,store=create.storeObject,host=this}:action).ToArray();
                foreach(var seller in station.GetComponentsInChildren<PlayMakerFSM>(true).Where(f=>f.FsmName=="SellItemsHere"))
                {
                    foreach(var state in seller.Fsm.States)
                    {
                        foreach(var action in state.Actions)if(action is GetParent parent)parent.gameObject=Owner(lookup.gameObject);
                        state.Actions=state.Actions.Select(action=>action is TriggerEventByLayer triggerAction?
                            (FsmStateAction)new MerchantTrigger{trigger=triggerAction.trigger,layer=triggerAction.layer,sendEvent=triggerAction.sendEvent,storeCollider=triggerAction.storeCollider}:action).ToArray();
                    }
                    _sellers.Add(seller);
                }
                var goods=Presentation?new GameObject[0]:MerchantCatalog.Goods(type);
                for(int i=0;i<4;i++)
                {var sale=Anchor(station.transform,"Sale"+i);sale.localPosition=new Vector3(0.4f+i%2*0.55f,0.1f,-0.5f-i/2*0.65f);Add(type+"/sale/"+i,MerchantCatalog.Label(type)+" sale item "+(i+1),"Sale items",sale);
                    if(!stock.Any(s=>s.type==type&&s.slot==i)&&goods.Length>0)stock.Add(new MerchantStock{type=type,slot=i,prefab=goods[UnityEngine.Random.Range(0,goods.Length)].name});}
                yield return null;
                Init(list);var demand=(Data.demands??new MerchantDemand[0]).FirstOrDefault(d=>d.type==type);
                if(demand!=null&&demand.names.Length==demand.prices.Length){SetDemand(list,demand);list.Fsm.SetState("done");}
                else if(!Presentation)list.Fsm.SetState(type=="organic"?"create":"random");
                foreach(var seller in station.GetComponentsInChildren<PlayMakerFSM>(true).Where(f=>f.FsmName=="SellItemsHere")){Init(seller);seller.Fsm.SetState("wait");}
            }
            Data.stock=stock.ToArray();
            if(Presentation){Ready=true;ApplyNetwork(Data,NetworkPoses,NetworkTrading);yield break;}
            MerchantLayouts.Apply(this);
            var wanted=new HashSet<string>(Data.stock.Where(s=>!s.purchased&&!string.IsNullOrEmpty(s.nativeName)).Select(s=>s.nativeName));var existingItems=new Dictionary<string,GameObject>();
            if(wanted.Count>0||discarded.Count>0)foreach(var item in Resources.FindObjectsOfTypeAll<GameObject>())if(item!=null&&item.scene.IsValid())
            {if(discarded.Contains(item.name)){Register.RemoveTree(item);Destroy(item);}else if(wanted.Contains(item.name))existingItems[item.name]=item;}
            foreach(var record in Data.stock)
            {
                if(record.purchased)continue;var sale=Objects.FirstOrDefault(o=>o.Id==record.type+"/sale/"+record.slot)?.Transform;if(sale==null)continue;
                GameObject existing;existingItems.TryGetValue(record.nativeName??"",out existing);
                var prefab=Prefabs.FindAny(record.prefab);if(existing==null&&prefab==null)continue;
                var item=existing??Instantiate(prefab,sale.position,sale.rotation);item.transform.SetParent(sale,true);item.transform.localPosition=Vector3.zero;item.transform.localRotation=Quaternion.identity;
                if(existing==null)Register.Name(item,prefab.name);record.nativeName=item.name;_stock[record]=item;
                var rb=item.GetComponent<Rigidbody>();if(rb!=null)rb.isKinematic=true;
                foreach(var c in item.GetComponentsInChildren<Collider>(true))foreach(var car in _chassis)if(c!=null&&car!=null&&c!=car)Physics.IgnoreCollision(c,car,true);
                yield return null;
                foreach(var f in item.GetComponents<PlayMakerFSM>())if(f.FsmName=="Value"||f.FsmName=="ForBuy"||f.FsmName=="Buyable"){Init(f);if(f.FsmName=="Value")f.Fsm.SetState("value");}
                var value=item.GetComponents<PlayMakerFSM>().FirstOrDefault(f=>f.FsmName=="Value")?.FsmVariables.FindFsmInt("BuyValue");if(value!=null){if(record.buyPrice>=0)value.Value=record.buyPrice;else record.buyPrice=value.Value;}
                var buy=item.GetComponents<PlayMakerFSM>().FirstOrDefault(f=>f.FsmName=="ForBuy");if(buy!=null){buy.FsmVariables.FindFsmInt("ForBuyInt").Value=1;buy.Fsm.SetState("forBuy");}
                if(record.hostilityReleased)
                {
                    if(TrafficFactions.PlayerHostile(GetComponent<PatrolMarker>()?.Faction))
                    {var interaction=Core.FsmUtil.Find(item,"Buyable");if(interaction!=null)interaction.SendEvent("FactionCoyotesEnemy");}
                    else record.hostilityReleased=false;
                }
                if(existing==null){Register.Add(item,false);Register.Commit(item);}
                item.SetActive(true);yield return null;
            }
            Park();Ready=true;
            MerchantGuards.Restore(this);
        }
        private static FsmOwnerDefault Owner(GameObject who)=>new FsmOwnerDefault{OwnerOption=OwnerDefaultOption.SpecifyGameObject,GameObject=new FsmGameObject{Value=who}};
        internal GameObject CreateProbe(GameObject prefab)
        {
            if(prefab==null)return null;
            if(_inactiveProbes==null){_inactiveProbes=Anchor(_root,"InactiveProbes");_inactiveProbes.gameObject.SetActive(false);}
            var probe=Instantiate(prefab,_inactiveProbes);probe.SetActive(false);
            // Metadata initialization needs ItemName and Value only. In particular, a bedroll's
            // ordinary spawn/unpack FSM must never create physical stock while the board is built.
            foreach(var f in probe.GetComponentsInChildren<PlayMakerFSM>(true))f.enabled=f.FsmName=="ItemName"||f.FsmName=="Value";
            foreach(var r in probe.GetComponentsInChildren<Renderer>(true))r.enabled=false;
            foreach(var c in probe.GetComponentsInChildren<Collider>(true))c.enabled=false;
            foreach(var rb in probe.GetComponentsInChildren<Rigidbody>(true))rb.isKinematic=true;
            probe.transform.SetParent(_root,false);probe.transform.localPosition=Vector3.zero;probe.SetActive(true);
            _probes.Add(probe);return probe;
        }
        private void CleanProbes()
        {
            if(_probes.Count==0||_lists.Any(f=>f!=null&&f.ActiveStateName!="done"))return;
            foreach(var probe in _probes)if(probe!=null){Register.RemoveTree(probe);Destroy(probe);}
            _probes.Clear();
        }
        private static void Init(PlayMakerFSM f){f.Fsm.InitData();f.Fsm.Preprocess(f);f.Fsm.Init(f);f.enabled=true;}
        private static Transform Anchor(Transform parent,string name){var node=new GameObject(name).transform;node.SetParent(parent,false);return node;}
        private void Add(string id,string label,string kind,Transform node)=>Objects.Add(new MerchantObject{Id=id,Label=label,Kind=kind,Transform=node});
        private static void SetDemand(PlayMakerFSM list,MerchantDemand demand)
        {
            list.FsmVariables.FindFsmArray("ItemNameArray").Values=demand.names.Cast<object>().ToArray();list.FsmVariables.FindFsmArray("ItemPriceArray").Values=demand.prices.Cast<object>().ToArray();
            var names=list.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="item_list");var prices=list.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="price_list");
            for(int i=0;i<demand.names.Length;i++){if(names!=null&&i<names.childCount)Text(names.GetChild(i),demand.names[i]);if(prices!=null&&i<prices.childCount)Text(prices.GetChild(i),demand.prices[i].ToString());}
        }
        private static void Text(Transform node,string text){var component=node.GetComponents<Component>().FirstOrDefault(c=>c!=null&&c.GetType().Name=="TextMeshPro");component?.GetType().GetProperty("text")?.SetValue(component,text,null);}
        internal void ApplyNetwork(MerchantSave data,MerchantPose[] poses,bool trading)
        {
            if(!ReferenceEquals(Data,data))
            {
                _deadFsms.Clear();foreach(var pair in _itemFsms)if(pair.Value.Item==null)_deadFsms.Add(pair.Key);
                foreach(int id in _deadFsms)_itemFsms.Remove(id);
            }
            Data=data;NetworkPoses=poses;NetworkTrading=trading;if(!Ready)return;
            foreach(var list in _lists){var demand=Data.demands.FirstOrDefault(d=>d.type==list.gameObject.name);if(demand!=null)SetDemand(list,demand);}
            foreach(var pose in poses??new MerchantPose[0]){var obj=Objects.FirstOrDefault(o=>o.Id==pose.id);if(obj?.Transform!=null){obj.Transform.position=transform.TransformPoint(pose.position);obj.Transform.rotation=transform.rotation*Quaternion.Euler(pose.rotation);}}
            foreach(var seller in _sellers)if(seller!=null&&seller.enabled!=trading){seller.enabled=trading;if(trading)seller.Fsm.SetState("trigger");}
        }
        private void Update()
        { if (Presentation || !PatrolsModule.Running) return;
            if(Time.time<_next||Data==null)return;_next=Time.time+0.5f;Park();
            if(_marker==null)_marker=GetComponent<PatrolMarker>();var marker=_marker;bool trading=Ready&&!TrafficFactions.PlayerHostile(Data.types.Length>0?marker?.Faction:"Coyotes");
            foreach(var f in _sellers)if(f!=null&&f.enabled!=trading){f.enabled=trading;if(trading)f.Fsm.SetState("trigger");}
            ObserveStock(trading);
            CleanProbes();
        }
        // Perf: each stock item's ForBuy / Value / Buyable FSMs found once per item (was GetComponents + LINQ x3 per item, 2/s)
        private sealed class ItemFsms{internal GameObject Item;internal PlayMakerFSM Buy,Value,Buyable,Name;}
        private readonly Dictionary<int,ItemFsms> _itemFsms=new Dictionary<int,ItemFsms>();
        private readonly List<int> _deadFsms=new List<int>();
        private static readonly List<PlayMakerFSM> _fs=new List<PlayMakerFSM>();
        private float _nextReconcile,_nextStock;
        internal static bool BeforeEvent(Fsm fsm,FsmEvent ev,FsmEventData data)
        {
            if(!PatrolsModule.Running||Core.Multiplayer.Client||ev==null||ev.Name!="FactionCoyotesEnemy"||fsm==null||fsm.Name!="Buyable"||fsm.GameObject==null)return true;
            var host=fsm.GameObject.GetComponentInParent<MerchantHost>();if(host==null||host.Presentation)return true;
            foreach(var pair in host._stock)
            {
                if(pair.Key.purchased||!ReferenceEquals(pair.Value,fsm.GameObject))continue;
                var sale=host.FsmsOf(pair.Value).Buy?.FsmVariables.FindFsmInt("ForBuyInt");
                // Purchase reconciliation arrives after setting zero; hostility arrives while stock is still for sale.
                if(sale!=null&&sale.Value>0)pair.Key.hostilityReleased=true;
                break;
            }
            return true;
        }
        private ItemFsms FsmsOf(GameObject item)
        {
            int id=item.GetInstanceID();if(_itemFsms.TryGetValue(id,out var c)&&ReferenceEquals(c.Item,item))return c;
            c=new ItemFsms{Item=item};_fs.Clear();item.GetComponents(_fs);
            foreach(var f in _fs){if(f==null)continue;if(c.Buy==null&&f.FsmName=="ForBuy")c.Buy=f;else if(c.Value==null&&f.FsmName=="Value")c.Value=f;else if(c.Buyable==null&&f.FsmName=="Buyable")c.Buyable=f;else if(c.Name==null&&f.FsmName=="ItemName")c.Name=f;}
            _fs.Clear();_itemFsms[id]=c;return c;
        }
        private static void ReconcileItem(ItemFsms fsms,bool forSale,int price)
        {
            var sale=fsms.Buy?.FsmVariables.FindFsmInt("ForBuyInt");if(sale==null||fsms.Buyable==null)return;
            if(forSale)
            {
                var value=fsms.Value?.FsmVariables.FindFsmInt("BuyValue");if(value!=null&&price>=0&&value.Value!=price)value.Value=price;
                if(sale.Value==0||!fsms.Buyable.enabled||fsms.Name!=null&&fsms.Name.enabled){sale.Value=1;fsms.Buy.Fsm.SetState("forBuy");}
            }
            else if(sale.Value!=0||fsms.Buyable.enabled||fsms.Name!=null&&!fsms.Name.enabled)
            {
                sale.Value=0;fsms.Buyable.enabled=true;fsms.Buyable.SendEvent("FactionCoyotesEnemy");
            }
        }
        internal void ReconcileStock(bool trading)
        {
            if(!Ready||Data==null||!Core.GameRefs.WorldRunning||Time.unscaledTime<_nextReconcile)return;_nextReconcile=Time.unscaledTime+3f;
            if(Presentation)
            {
                foreach(var record in Data.stock)
                {
                    var item=record.objectId>0?Core.Multiplayer.ObjectById(record.objectId):null;if(item==null)continue;
                    var fsms=FsmsOf(item);var sale=fsms.Buy?.FsmVariables.FindFsmInt("ForBuyInt");if(sale==null)continue;
                    // A zero on a friendly guest may be a purchase awaiting host confirmation; never undo it here.
                    ReconcileItem(fsms,!record.purchased&&trading&&sale.Value>0,record.buyPrice);
                }
                return;
            }
            foreach(var pair in _stock)
            {
                var item=pair.Value;if(item==null)continue;
                if(!pair.Key.purchased&&!trading)pair.Key.hostilityReleased=true;
                ReconcileItem(FsmsOf(item),!pair.Key.purchased&&trading,pair.Key.buyPrice);
            }
        }
        private void ObserveStock(bool trading)
        {
            if(!Ready||!Core.SaveBus.Busy&&Time.unscaledTime<_nextStock)return;_nextStock=Time.unscaledTime+3f;
            foreach(var pair in _stock)
            {
                var item=pair.Value;if(pair.Key.purchased)continue;if(item==null){pair.Key.purchased=true;continue;}
                var fsms=FsmsOf(item);var buy=fsms.Buy!=null?fsms.Buy:null;
                if(pair.Key.hostilityReleased&&item.transform.IsChildOf(transform)&&(!Core.Multiplayer.Active||Core.Multiplayer.OwnerOf(item)==0))
                {
                    if(!trading)continue;
                    var sale=buy?.FsmVariables.FindFsmInt("ForBuyInt");
                    if(sale!=null)
                    {
                        var price=fsms.Value?.FsmVariables.FindFsmInt("BuyValue");if(price!=null&&pair.Key.buyPrice>=0)price.Value=pair.Key.buyPrice;
                        sale.Value=1;buy.Fsm.SetState("forBuy");pair.Key.hostilityReleased=false;
                        if(Core.Log.Verbose)Core.Log.Info("[Patrols] Merchant stock returned to sale after hostility: "+item.name);
                    }
                }
                if(Ready&&buy?.FsmVariables.FindFsmInt("ForBuyInt")?.Value==0){pair.Key.purchased=true;item.transform.SetParent(null,true);foreach(var a in item.GetComponentsInChildren<Collider>(true))foreach(var b in _chassis??new Collider[0])if(a!=null&&b!=null&&a!=b)Physics.IgnoreCollision(a,b,false);continue;}
                var value=fsms.Value!=null?fsms.Value.FsmVariables.FindFsmInt("BuyValue"):null;if(value!=null)pair.Key.buyPrice=value.Value;
                var buyable=fsms.Buyable;if(buyable!=null&&buyable.enabled!=trading)buyable.enabled=trading;
            }
            ReconcileStock(trading);
        }
        internal string Snapshot()
        {
            MerchantGuards.Capture(this);
            ObserveStock(Ready&&!TrafficFactions.PlayerHostile(GetComponent<PatrolMarker>()?.Faction));
            foreach(var record in Data.stock)record.objectId=0;
            foreach(var pair in _stock)if(pair.Value!=null)pair.Key.objectId=Core.Multiplayer.ObjectId(pair.Value);
            Data.demands=_lists.Select(f=>new MerchantDemand{type=f.gameObject.name,names=(f.FsmVariables.FindFsmArray("ItemNameArray")?.Values??new object[0]).Select(Convert.ToString).ToArray(),prices=(f.FsmVariables.FindFsmArray("ItemPriceArray")?.Values??new object[0]).Select(Convert.ToInt32).ToArray()}).Where(d=>d.names.Length>0&&d.names.Length==d.prices.Length).ToArray();
            return TrafficCodec.WriteRecord(Data);
        }
    }
    internal sealed class MerchantProbeAction : FsmStateAction
    {
        public FsmGameObject source,store;
        public MerchantHost host;
        public override void OnEnter(){store.Value=host.CreateProbe(source.Value);Finish();}
    }
    internal sealed class MerchantTrigger : TriggerEventByLayer
    {
        public override void OnPreprocess(){base.OnPreprocess();if(trigger==TriggerType.OnTriggerEnter)Fsm.HandleTriggerStay=true;}
        static bool Local(Collider other){return other!=null&&(!Core.Multiplayer.Active||Core.Multiplayer.ItemAuthority(other.gameObject));}
        public override void DoTriggerEnter(Collider other){if(other!=null&&Core.Multiplayer.Client&&!Local(other))Core.Multiplayer.RequestItemAuthority(other.gameObject);if(Local(other))base.DoTriggerEnter(other);}
        public override void DoTriggerStay(Collider other){if(!Local(other))return;if(trigger==TriggerType.OnTriggerEnter)base.DoTriggerEnter(other);else base.DoTriggerStay(other);}
    }
}
