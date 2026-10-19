using System;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Guns
{
    /// <summary>
    /// Core.Services.Guns adapter (ex Gunplay.Api v1). Kind codes: pistol=0, SMG=1, rifle=2, sniper=3, shotgun=4, crossbow=5.
    /// Consumers: NPC (WeaponRanges, VanillaEvents) and Patrols (HandlesFittedWheelMelee).
    /// </summary>
    internal sealed class GunsApi : IGunsApi
    {
        public int ContractVersion { get { return 1; } }
        public bool ProjectilesEnabled { get { return GunsModule.ProjectilesEnabled; } }
        public bool PlayerProjectilesEnabled { get { return GunsModule.ProjectilesEnabled && GunsModule.PlayerTracers.Value; } }
        public bool BlocksProjectile(Collider collider) { return collider != null && Tracers.Counts(collider); }
        public float EffectiveRange(int kind) { return Tracers.RangeOf((Tracers.Kind)kind); }
        public bool TryGetWeaponKind(GameObject owner, out int kind)
        {
            Tracers.Kind found = Tracers.Kind.Rifle;
            bool result = owner != null && Tracers.GunKindOf(owner, out found);
            kind = result ? (int)found : 2;
            return result;
        }
        public bool HandlesFittedWheelMelee { get { return GunsModule.FittedWheelMeleeActive; } }
    }

    /// <summary>
    /// Calls into the other modules (was Integration.cs: AppDomain scan for NPCAI.Api / GunplayHUD.Api every 5 s + delegates).
    /// Every call null-checks the Core service and falls back exactly like the standalone mod did without the sibling mod.
    /// </summary>
    internal static class Bridge
    {
        internal static float SpreadFactor(float distance)
        {
            var npc = Services.Npc;
            if (npc != null)
            {
                try { return npc.SpreadFactor(distance); }
                catch (Exception e) { Core.Log.Warn("[Guns] NPC spread: " + e.Message); }
            }
            float beyond = distance - 5f;
            return 1f + (beyond <= 0f ? 0 : (int)(beyond / 5f)) * 0.1f;
        }

        internal static void Shot(GameObject shooter, Vector3 position, int kind, bool player)
        {
            Services.ReportCombat();
            var npc = Services.Npc; if (npc == null) return;
            try { npc.Shot(shooter, position, kind, player); } catch (Exception e) { Core.Log.Warn("[Guns] NPC shot: " + e.Message); }
        }

        internal static void Hurt(GameObject victim, GameObject attacker)
        {
            Services.ReportCombat();
            var npc = Services.Npc; if (npc == null) return;
            try { npc.Hurt(victim, attacker); } catch (Exception e) { Core.Log.Warn("[Guns] NPC hurt: " + e.Message); }
        }

        internal static void PlayerHit(GameObject target, Vector3 position, float damage, bool head)
        {
            var hud = Services.Hud; if (hud == null) return;
            try { hud.PlayerHit(target, position, damage, head); } catch (Exception e) { Core.Log.Warn("[Guns] HUD hit: " + e.Message); }
        }

        internal static void PartHit(GameObject target, Vector3 position, float damage)
        {
            var hud = Services.Hud; if (hud == null) return;
            try { hud.PartHit(target, position, damage); } catch (Exception e) { Core.Log.Warn("[Guns] HUD part hit: " + e.Message); }
        }
    }
}
