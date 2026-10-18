# Remote holding poses, NPC arm fit and equipment — 2026-10-18, Testing

Denis supplied paired screenshots of the same female player: the observer saw an M16 raised across her face while the local third-person model held it lowered. He also reported missing equipped backpacks/items and requested an NPC animation audit. He explicitly approved all three fixes and the larger PlayerBody/Core handshake scope.

## Proven causes

- MultiplayerAvatars set `character.Aiming = item != null`, forcing the aiming clip for every held weapon. The existing mode-4 final-pose stream ran only during reload/throw/cocking. Local Body instead selects lowered/aim/fire poses from actual input.
- ModAPI.Character reads the same GunPose and AimLift tables as local Body, but applied only the first three aim-lift values. Local Body also blends the ten left/right arm values by clip weight. The installed M16 aim-lift entries have nonzero arm adjustments (including approximately -17.5/-17.7 cm vertical corrections), proving a relevant fit difference. NPC Brain.Drive already selects aiming from combat state; it does not have the remote player's always-aim bug.
- Native PlayerState serializes the held item/actions but no worn/holstered visual loadout. WW's Equipment was constructed only for local Body. Native saved loadout/player recovery is separate from this visual path.

Evidence: `src/Modules/PlayerBody/Animation/{MultiplayerAvatars,ModCharacter,MultiplayerMotion}.cs`, `Animation/Loco.cs` ApplyArmPose/LateMixamo, `src/Modules/NPC/Brain/Brain.cs` Drive; existing external `V:\Repos\Apocalypter\_staging\ww-mp-20261018\inspection\ApocalypterMP.decompiled.cs` PlayerState/PlayerBody/ItemModels/WorldObjects.FindPrefab; native `Sources/systems/items_weapons.md` physical weapon/QuickItems holders. Current logs confirm PlayerBody active and M16/backpack spawning. Installed pre-fix DLL MD5 was `B5A87FE45A1CB52887FD6902FD65F0E9`.

## Implementation

- Existing 20 Hz mode 4 now captures whenever a local held prop exists, as well as the existing special weapon actions. Receivers use the sender's actual bones and held-weapon transform before camera-only first-person fitting, including its own tuned pose files. Generic fallback no longer forces aiming. Existing mode-1 climbing and mode-2/3 seated ownership retain priority.
- ModAPI.Character applies all ten per-arm values after aim lift and before weapon positioning, using the same weighted clip selection/order/IK as Body. This reaches NPCs through the existing animation service; no NPC source file changes.
- New MultiplayerEquipment uses unused WW message 83 for nine existing mount identities plus a nine-bit drawn/hidden mask: backpack, two long guns, two sidearms, two blades, binoculars and flashlight. Sender reads existing Equipment mounts at the existing 0.2-second character tick, sends on change and repeats every three seconds for late joiners. Host validates the peer/epoch and relays reliable snapshots. Receivers resolve native cached prefabs only when identities change or are unavailable, then reuse Equipment's original offsets/mesh stripping. Duplicate types retain separate mount masks. Remote flashlight mounts have no added local-light glow. EquipmentOnBody is respected; model rebuild/session/epoch/shutdown clear or reapply mounts.
- Handshake revision **10** requires all peers to update. Seven source files, **167 added/16 removed lines** for this task, including the new 88-line file. No new Harmony target/job, config entry, persistent save format, external DLL or base-repo change. Existing config/custom profiles are preserved. Broader steady-state pose traffic now applies whenever a weapon is held; runtime cost remains unmeasured.

## Build and deployment

Windows `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=true` passed with **zero warnings/errors**. Both games were closed and disabled WW manifests absent before deployment; the client plugins junction shares the host install. Built/installed/unique stage MD5: **`D6ED04A0530F7E879D43DBB46BD2991B`**.

Stage: `bin/Release/WildWastelandMP_poses-equipment_20261018_215044.dll`. Installed: `E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\WildWastelandMP\WildWastelandMP.dll`. No game launch, harness, commit or push. Runtime fit/animation/network performance remain **Testing**.

## Denis's retest

1. Restart both peers, host/join and repeat the screenshot with the M16 held without aiming. Observe each other with the sender in both camera modes. Expect the observer's weapon/arms to follow the sender's lowered pose, then its actual aim/fire pose when those actions start. Repeat standing, walking, running and crouching with a pistol, shotgun, crossbow and melee weapon.
2. Reload/fire/cock, quick-throw a grenade, throw a lance, kick and change/holster weapons. Regress climbing and driver/passenger hands. Expect no stuck/phantom action or weapon-transform jump after returning to normal locomotion.
3. Equip a backpack and several weapons, binoculars and a flashlight. Expect the other peer to see the same existing local mount arrangement. Draw/holster/drop/change each item; only its drawn mount should disappear, with the other mounts retained. Try two weapons of the same type, backpack removal, EquipmentOnBody off/on, reconnect, character switch and a late join. Periodic refresh may take up to three seconds. With a third peer, observe guest equipment from another guest.
4. Observe rifle NPCs in relaxed movement, standing/crouched aim, bursts and reloads on both peers. Expect the current per-arm fit to apply, retaining their combat-dependent aim/relax behavior. Test native and female models; skeleton/proportion differences still need visual acceptance.

No new diagnostic log lines. If a visual fails, report the weapon/action, sender/observer role and camera mode and retain host/client logs. Packet/animation errors would appear as existing `Rejected WW packet`, `ModAPI ...` or `LateUpdate` errors; their absence alone does not certify visual correctness.
