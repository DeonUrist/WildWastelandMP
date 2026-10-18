using System.IO;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Patrols
{
    public sealed partial class PatrolsModule : IMultiplayerPresentation
    {
        void IMultiplayerPresentation.InitClient()
        {
            // Clients need the same custom native templates, without a traffic engine or a second crew brain.
            WarRig.WarRigFeature.Dir = Path.Combine(Dir, "WarRig");
            Multiplayer.PrefabRequested -= NetworkPrefab;
            Multiplayer.PrefabRequested += NetworkPrefab;
            Multiplayer.CrewPresentationRequested -= Rider.NoContacts; Multiplayer.CrewPresentationRequested += Rider.NoContacts;
            WorldTraffic.StartNetwork(this, true);
            Ram.StartNetwork();
            Services.Camps = new WorldTraffic.NetworkCampStatus();
            Scheduler.EveryFrame(this, "Ram movement recovery", Ram.Tick);
        }
        void IMultiplayerPresentation.ShutdownClient() { if (Services.Camps is WorldTraffic.NetworkCampStatus) Services.Camps = null; Ram.StopNetwork(); WorldTraffic.StopNetwork(); Multiplayer.PrefabRequested -= NetworkPrefab; Multiplayer.CrewPresentationRequested -= Rider.NoContacts; }
        static GameObject NetworkPrefab(string name)
        {
            if (name == WarRig.Factory.Name) return WarRig.Factory.Template();
            if (name == MotorcycleIntegration.Body) return MotorcycleIntegration.Template();
            return null;
        }
    }
}
