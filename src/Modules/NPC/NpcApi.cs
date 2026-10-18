using System.Collections.Generic;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.NPC
{
    /// <summary>
    /// Core.Services.Npc: NPCAI.Api v1 (same members, same behaviour) plus the nav structure list Apocapatrol used to read from
    /// NPCAI.Nav._structures by reflection. Set in NpcModule.Init, cleared in Shutdown.
    /// </summary>
    internal sealed class NpcApi : INpcApi
    {
        public int ContractVersion { get { return 1; } }
        public void SetSimpleConvoyBandit(GameObject npc, bool enabled) { SimpleBandits.Set(npc, enabled); }
        public void SetSimpleConvoyBanditState(GameObject npc, bool enabled, bool combat) { SimpleBandits.Set(npc, enabled, combat); }
        public void SetSimpleConvoyBanditTarget(GameObject npc, GameObject target) { SimpleBandits.Target(npc, target); }
        public void Shot(GameObject shooter, Vector3 position, int weaponKind, bool player)
        { if (weaponKind >= 0 && weaponKind <= 5) Senses.Shot(shooter, position, (WeaponRanges.Kind)weaponKind, player); }
        public void Hurt(GameObject victim, GameObject attacker) { Senses.Hurt(victim, attacker); }
        public float SpreadFactor(float distance) { return NpcModule.AimEnabled.Value ? Aim.SpreadFactor(distance) : 1f; }
        public void Inform(GameObject npc, GameObject target) { Senses.Inform(npc, target); }
        public void SetFollowerDriven(GameObject npc, bool enabled, float reloadUntil = 0f) { Brain.SetFollowerDriven(npc, enabled, reloadUntil); Idle.SetFollowerDriven(npc, enabled); }
        public void Structures(List<NavStructure> into) { Nav.Structures(into); }
        public bool HighlightMissingPoiBake(string prefabName) { return Nav.HighlightMissingPoiBake(prefabName); }
        // (crew AI) Idle's travel leg (Idle.Travel), the senses' idle test and fought-player memory
        public void Travel(GameObject npc, Vector3? scenePoint, float arriveRadius) { Idle.Travel(npc, scenePoint, arriveRadius); }
        public int TravelState(GameObject npc) { return Idle.TravelState(npc); }
        public bool IsIdle(GameObject npc) { return Senses.IsIdle(npc); }
        public void Remember(GameObject npc, GameObject target, float seconds) { Senses.Remember(npc, target, seconds); }
        public NpcPlayerSight ObservePlayers(GameObject observer, GameObject vehicle, Transform facing, bool hostile, float memorySeconds)
        { return Senses.ObservePlayers(observer, vehicle, facing, hostile, memorySeconds); }
        public void SetSeatedTarget(GameObject observer, GameObject target) { Senses.SetSeatedTarget(observer, target); }
        public void TransferPlayerAwareness(GameObject source, GameObject destination, float seconds)
        { Senses.TransferPlayerAwareness(source, destination, seconds); }
    }
}
