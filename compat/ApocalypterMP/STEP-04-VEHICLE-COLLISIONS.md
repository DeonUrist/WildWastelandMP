# Step 4 — multiplayer vehicle collisions

Completed in source and compiled; **paused after this step**. No deployment, game/server launch, commit or push. This is a collision checkpoint, not completion of vehicle-part state synchronization or the full MP patch.

## Proven cause

- Patrols Ram.Sensor classified only PlayerRef's local player/car; Ram.Hit always applied damage and shove to that local player. Its one-second cooldown was keyed only by the striking car, so simply looping players would suppress everyone after the first victim.
- MP guest cars have two creature damage paths: ClearTheWay -> SendCarHit and CarAttack -> OnClientHit(byCar). The shortcut computes raw native bumper damage and bypasses WW's NPC ram multiplier. Both paths can report the same physical impact. Host mirrors must not also damage the creature independently.
- Ram.AfterSetFsmFloat excluded the host player from creature scaling, but did not exclude remote player presences.

Evidence: supplied MP source in the external dossier (`WorldObjects.ClientCarUpdate`, `ClearTheWay`, `SendCarHit`, `OnClientHit`, `PassiveCarDisabledFsms`, `IsCarSimulatedHere`), Patrols Ram/Pilot source, and vanilla `Sources/fsm/prefabs/Rustallion.md` CarAttack/BumperCheck. Native bumper damage is signed negative and CarAttack requires speed >5 m/s.

## Changes

- A host-simulated patrol car identifies the actual remote body or occupied vehicle it collided with. Host and guests sharing a car are handled separately. Cooldowns now use striking-car/victim pairs; pilot collision classification recognizes remote players/cars without granting perception or visual acquisition.
- Host computes each guest's existing WW ram damage and, on foot, shove velocity. Reliable WW kind 60 targets that guest only. The guest applies its own native Bodypart/armor/health/effects and movement, preserving native downed/revival ownership and checking local god mode. A client presentation job resumes Movement after the existing half-second shove; shutdown/session reset also restores it.
- Core intercepts both native guest-car damage paths. WW kind 61 sends car ID, creature ID and speed rather than a client-computed damage amount. Host checks the live sender, world readiness, car ownership, creature identity, own-crew exclusion, proximity, exact payload and finite speed in (5, 200] m/s. It then reads its own car's BumperDamage, applies the host's NPC ram multiplier, records the actual player in WW hurt memory and dispatches one native Bodypart damage event.
- The host deduplicates guest-car hits for one second per car/victim. Host-mirror CarAttack writes/events for remotely owned cars are suppressed by Patrols' existing shared hooks. Native MP already disables passive CarAttack; the guard also covers transitional callbacks. MP's native ClearTheWay collision-ghost presentation remains intact.
- Remote player presences are excluded from the creature-only ram multiplier. Existing ram switches, in-car factor, body table, speed threshold, god mode and host/local behavior remain in place. No new config entries, save keys, prefab IDs or saved components.

Approximately 230 added/changed source lines across new Core/MultiplayerCombat.Cars.cs, new Patrols/Crews/Ram.Network.cs, existing Multiplayer/MultiplayerCombat, Patrols Ram/Pilot/lifecycle and client presentation. AGENTS.md records the native damage paths and ItemVars revert behavior for later sessions.

## Validation and checkpoint

PowerShell: `dotnet build WildWasteland.csproj -c Release -p:Deploy=false -v:minimal` — **0 warnings, 0 errors**, 2026-10-18. Compilation only. Runtime Harmony bindings, physical contact delivery and networking remain unverified.

- DLL MD5: `37CBFE40FB1F2B1061B25301C8DA0D38`.
- DLL SHA256: `5227DC87074F3F269FEFAB6053E664A17924A56DB729AC834817F34A3E018525`.
- Non-live DLL: `V:\Repos\Apocalypter\_staging\ww-mp-20261018\WildWasteland_0.1.36_mp_step4_37CBFE40.dll`.
- Source checkpoint: external `rc33-step4/`, including source-overlay, RC33-step4-source-overlay.zip, FILES.txt and tracked.patch. The overlay includes new untracked source/docs; the patch contains tracked modifications only. Use the overlay on clean base `ae5d83851f1f046babc02229e85c544b1eeab83a`; do not apply both overlay and patch.
- Main remains clean at the base. Live game DLL/configs were not touched. The DLL includes the earlier unfinished compatibility draft.

## Manual tests after the complete patch is ready

All peers need the matching WW patch, MP DLL and enabled Patrols presentation/host module. World-setting/module-master enforcement remains part of the broader settings/handshake audit.

1. Have an AI patrol hit guest B on foot while host A is elsewhere. Only B should take configured damage and shove; B should regain normal movement after 0.5 seconds. Host A's health must not change.
2. Put A and B in the same car and let a hostile patrol strike it. Each eligible occupant should receive one configured in-car hit, with no foot shove. Disable RamDamageInCar and repeat: neither should receive the WW ram hit. Test two guests in the same vehicle as well.
3. Drive a guest-owned car into an NPC with NPC ram multiplier 0, 1 and 4. Expect no damage at 0, native speed x bumper damage at 1 and four times that at 4. Multiple colliders/forwarding paths must not create two hits within one second for that car/victim.
4. Carry a crew member in the striking car and repeat. Its own crew must not be ram-damaged. Repeat after entering/leaving the car and after dismounting that NPC; genuine dismounted victims remain damageable.
5. Down/revive a guest, disconnect, and stop/restart the module after a shove. No guest should remain with Movement disabled. A parked patrol or a collision below the configured threshold should not damage players.

Host global VerboseLog confirms `[Patrols] Ram: <car> -> player <peer>, <amount> damage` and `[Patrols] Ram: player <peer> in <car> -> <victim>, <amount> damage`. Guest damage uses the existing `Ram from <car>: ... damage to the player` log. Debug controls remain host-only; enable desired logs before later testing through the authorized host settings.

## Limits and immediate next step

The guest car request validates ownership, victim and bounded speed/proximity; it is not a host replay of physics contacts and still receives a client speed. Packet kinds 60/61 are internal WW additions under its existing type-250 envelope. Native own-crew ghost presentation is unchanged. Physical contact callbacks and timing require game testing.

Next is [PART-STATE-CHECKLIST.md](PART-STATE-CHECKLIST.md): condition and other engine/part variables, real attached blast lances and armor plates, door/trunk/hood open state, ownership transfers, late join and saving/loading. HitWorld and fitted-wheel melee mutations on another owner's parts still need implementation. Native ItemVars.ItemsTick can revert these changes; registering an object alone does not solve it. Custom monster orb/rider explosion propagation, bolt attachments and reliable host weapon/trajectory validation also remain open. Resume only when asked, complete one important step, build Deploy=false, document and pause.
