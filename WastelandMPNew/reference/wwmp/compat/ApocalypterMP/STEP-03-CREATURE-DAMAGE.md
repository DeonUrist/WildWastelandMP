# Step 3 — creature damage authority and party kill progress

Completed in source; **paused after this step**. Windows Release compilation passed. No deployment, game/server launch, commit or push. The full compatibility patch remains unfinished.

## Proven cause

The supplied MP protocol 21 forwards client hits as `CreatureDamage` (type 16): tracked object ID, relative body-part path and signed damage. Its `NetSession.Handle(from, data)` calls `WorldObjects.OnCreatureDamage(reader)` without the sender. That handler writes Bodypart.Damage and sends Damage directly, bypassing WW's SetFsmFloat observation. WW therefore could not attribute those remote hits to the actual guest for hurt memory/faction reactions. WW projectile Replay also forwarded the hit and then dispatched the same damage event locally. Native MP disables much client creature simulation, but WW must not rely on that to enforce damage authority.

Native shared quest kill processing still checked only the host player's distance. A guest fighting far away from the host could not progress the party's Kill/Clear/Takeover objective. Seated WW gunmen also used the car's hierarchy root as their bullet shooter ID.

Evidence: existing `inspection/ApocalypterMP.decompiled.cs` in the external dossier, `ForwardDamagePatch`, `NetSession.Handle`, `WorldObjects.OnClientHit`, `OnCreatureDamage`, `SendDamage`; vanilla `Sources/systems/npc_ai.md` documents Bodypart -> Health and head multipliers. No additional decompilation was performed.

## Changes

- New Core MultiplayerCombat inspects native creature-hit packets in the existing Handle prefix before allowing the unchanged native damage handler. It requires a present sender, ready host world, bounded/exact packet, finite negative damage, an active tracked creature, a valid descendant Bodypart/Damage variable and living Health. It notifies WW NPC hurt logic with the actual peer's presence before the native hit can kill the victim. An NPC observation exception is logged without dropping a valid hit.
- Shared ProcessEvent guard suppresses Bodypart/Health Damage events on replicated client creatures. Native SendEvent forwards vanilla hits first. WW Replay forwards explicitly and skips its local event. Local player damage/revival and native creature snapshot application retain their separate native paths. Network visual shots still apply no damage.
- Host WW NPC bullets notify hurt logic before the event and identify a seated shooter by its nearest creature owner instead of the car. Offline event order remains unchanged.
- The host runs the existing quest death callback once and accepts a death near any party member, including a guest when the host player transform is absent. Existing quest matching, protected factions and party payout remain unchanged. This preserves the base rule that a nearby death can count, rather than introducing a requirement that a specific player landed the killing blow.

About 85 added/changed source lines across `Core/Multiplayer.cs`, new `Core/MultiplayerCombat.cs`, `Guns/Tracers/Tracers.cs` and `Quests/Plugin.cs`. No new config entries, save keys, prefab IDs or wire format.

## Validation and artifact

PowerShell: `dotnet build WildWasteland.csproj -c Release -p:Deploy=false -v:minimal` — **0 warnings, 0 errors**, 2026-10-18. Compilation only; reflection hooks and network behavior require Denis's later gameplay test.

- DLL MD5: `44C1490E195F6B32DA0F33E9B6BEDC25`.
- DLL SHA256: `DAA3C2BDFFAC27D0CDAD9275ED0C04F4840CAD0D37EB4E1E5F5A4937343E76D3`.
- Non-live DLL: `V:\Repos\Apocalypter\_staging\ww-mp-20261018\WildWasteland_0.1.36_mp_step3_44C1490E.dll`.
- Source checkpoint: `V:\Repos\Apocalypter\_staging\ww-mp-20261018\rc33-step3\`. Its source overlay includes all current tracked modifications and untracked compatibility source/docs; tracked.patch contains only tracked changes. Apply the overlay to clean base `ae5d83851f1f046babc02229e85c544b1eeab83a` to reconstruct this unfinished draft. Do not combine the patch and overlay.
- Primary main is clean at that base. Live DLL/configs were not touched.

## Manual scenarios after the complete patch is ready

1. Guest hits an on-foot NPC with melee, body shots and head shots. Confirm one host health decrement per hit with the configured WW falloff/head rules, then observe the same result on both clients. Repeat on a seated driver/passenger.
2. Hide host A; let guest B hit an NPC. Hurt memory/reaction must refer to B, without granting knowledge of A. Repeat with two guests hitting separately.
3. Guest completes a party Kill/Clear/Takeover objective outside the host's kill-credit radius. Progress should advance once per death on every quest log. Completing a paid quest should grant the full existing party reward to each separate wallet.
4. Seated NPC gunfire should render from the NPC on other peers and damage its actual target. Down/revive a guest and repeat ordinary NPC damage; player damage/revival must remain native.

Host F6 global VerboseLog adds `[Multiplayer] Creature hit: player <peer> -> <creature>/<path>, damage <value>` for accepted requests. NPC F7 VerboseLog remains necessary for detailed perception logs. No gameplay verification is claimed.

## Remaining combat work / next step

This step validates packet structure, sender and victim; **it does not derive damage from the host's weapon state or validate a shot trajectory**. The native protocol supplies a client damage value and no shot identity. Visual shot relays are unreliable, so treating their arrival as mandatory proof would reject legitimate damage. Host weapon/shot validation needs a deliberate reliable protocol and remains open.

Next important step: WW vehicle combat authority. HitWorld condition writes and fitted-wheel melee do not yet route mutations to another peer's owned parts. Native MP ClearTheWay/SendCarHit and OnClientHit(byCar) have a shared one-second throttle but different damage paths; preserve WW ram settings/own-crew protection without double hits. Patrols Ram.Sensor/Hit still targets the local host player, and native ram scaling must exclude remote player presences as it excludes the host. Custom monster orb presentation/trigger damage, bolt attachments and weapon/part state persistence also need audit. Full feature replication is not complete.
