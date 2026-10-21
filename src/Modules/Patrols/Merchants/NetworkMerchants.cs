using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Patrols
{
    internal static class NetworkMerchants
    {
        sealed class Shop { internal MerchantSave Data; internal MerchantPose[] Poses; internal bool Trading; }
        static MultiplayerSnapshot _network;
        static Dictionary<int, Shop> _shops = new Dictionary<int, Shop>();
        static readonly Dictionary<int, MerchantHost> Copies = new Dictionary<int, MerchantHost>();
        static readonly List<int> Removed = new List<int>();
        static int _generation = -1;
        internal static void Start(IModule owner)
        {
            Stop(); if (!Multiplayer.Present) return;
            _network = new MultiplayerSnapshot(owner, "Merchant fixtures", 154, 155, Capture, Apply);
            Multiplayer.SessionChanged += Reset;
            Scheduler.Interval(owner, "Merchant guest binding", .5f, Bind);
            Log.Always("[Multiplayer] Merchant fixture mirror ready (host layouts/stock; owner-only native sales)");
        }
        internal static void Stop()
        { if (_network != null) _network.Dispose(); _network = null; Multiplayer.SessionChanged -= Reset; Reset(); }
        static void Reset()
        { foreach (var host in Copies.Values) if (host != null) UnityEngine.Object.Destroy(host); Copies.Clear(); _shops.Clear(); }
        static void Point(BinaryWriter w, Vector3 p) { w.Write(p.x); w.Write(p.y); w.Write(p.z); }
        static Vector3 Point(BinaryReader r)
        { var p = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); if (float.IsNaN(p.sqrMagnitude) || float.IsInfinity(p.sqrMagnitude)) throw new InvalidDataException("Invalid merchant pose"); return p; }
        static byte[] Capture()
        {
            using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
            {
                int count = 0; foreach (var host in MerchantHost.All) if (host != null && host.Ready && !host.Presentation && Multiplayer.ObjectId(host.gameObject) != 0) count++;
                w.Write(count);
                foreach (var host in MerchantHost.All)
                {
                    if (host == null || !host.Ready || host.Presentation) continue;
                    int id = Multiplayer.ObjectId(host.gameObject); if (id == 0) continue;
                    w.Write(id); w.Write(host.Snapshot());
                    var marker = host.GetComponent<PatrolMarker>(); w.Write(!TrafficFactions.PlayerHostile(marker != null ? marker.Faction : "Coyotes"));
                    w.Write(host.Objects.Count);
                    foreach (var obj in host.Objects) { w.Write(obj.Id); Point(w, host.transform.InverseTransformPoint(obj.Transform.position)); Point(w, (Quaternion.Inverse(host.transform.rotation) * obj.Transform.rotation).eulerAngles); }
                }
                return stream.ToArray();
            }
        }
        static void Apply(byte[] data)
        {
            var shops = new Dictionary<int, Shop>();
            using (var r = new BinaryReader(new MemoryStream(data, false)))
            {
                int count = r.ReadInt32(); if (count < 0 || count > 128) throw new InvalidDataException("Invalid merchant count");
                for (int i = 0; i < count; i++)
                {
                    int id = r.ReadInt32(); string json = r.ReadString(); if (json.Length > 131072) throw new InvalidDataException("Merchant record too large");
                    var shop = new Shop { Data = TrafficCodec.ReadRecord<MerchantSave>(json), Trading = r.ReadBoolean() };
                    if (shop.Data == null || shop.Data.version != 1 || shop.Data.types == null || shop.Data.types.Length != 1 || !MerchantCatalog.Known(shop.Data.types[0]) || shop.Data.demands == null || shop.Data.stock == null) throw new InvalidDataException("Invalid merchant record");
                    if (shop.Data.stock.Length > 36) throw new InvalidDataException("Invalid merchant stock");
                    foreach (var stock in shop.Data.stock) if (stock == null || stock.objectId < 0) throw new InvalidDataException("Invalid merchant item");
                    foreach (var demand in shop.Data.demands) if (demand == null || demand.names == null || demand.prices == null || demand.names.Length != demand.prices.Length || demand.names.Length > 64) throw new InvalidDataException("Invalid merchant demand");
                    int poses = r.ReadInt32(); if (poses < 0 || poses > 64) throw new InvalidDataException("Invalid merchant layout");
                    shop.Poses = new MerchantPose[poses];
                    for (int p = 0; p < poses; p++) { string key = r.ReadString(); if (key.Length > 128) throw new InvalidDataException("Invalid merchant part"); shop.Poses[p] = new MerchantPose { id = key, position = Point(r), rotation = Point(r) }; }
                    shops.Add(id, shop);
                }
                if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("Trailing merchant data");
            }
            _shops = shops; Bind();
        }
        static void Bind()
        {
            if (!Multiplayer.Client || !Multiplayer.WorldReady) return;
            if (_generation != SaveBus.Generation) { _generation = SaveBus.Generation; Prefabs.Invalidate(); }
            Removed.Clear();
            foreach (var pair in Copies) if (!_shops.ContainsKey(pair.Key) || pair.Value == null)
            { if (pair.Value != null) UnityEngine.Object.Destroy(pair.Value); Removed.Add(pair.Key); }
            foreach (int id in Removed) Copies.Remove(id);
            foreach (var pair in _shops)
            {
                MerchantHost host;
                if (!Copies.TryGetValue(pair.Key, out host))
                {
                    var car = Multiplayer.ObjectById(pair.Key); if (car == null || Prefabs.FindAny(MerchantCatalog.Station(pair.Value.Data.types[0])) == null) continue;
                    host = car.AddComponent<MerchantHost>(); host.Presentation = true; Copies.Add(pair.Key, host);
                }
                host.ApplyNetwork(pair.Value.Data, pair.Value.Poses, pair.Value.Trading);
                host.ReconcileStock(pair.Value.Trading);
            }
        }
    }
}
