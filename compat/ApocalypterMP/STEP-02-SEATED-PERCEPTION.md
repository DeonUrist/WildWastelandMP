# Step 2 — seated gunners, drivers and dismount knowledge

Status: source checkpoint compiled; paused after this important step as requested. No deployment, commit, push or game/server launch. Overall multiplayer compatibility remains incomplete.

## Cause and change

Seated gunners previously received a shared faction target, and vehicle pursuit could choose the globally nearest player. A single host-only contact timestamp was also used when crew dismounted. Those paths could target an unseen player or hand out awareness belonging to another actor/player.

- Added a host WW perception service for each seated actor. It reuses independent player notice/acquisition/memory, sight cone, tracked-target behavior, range, night/storm and head/body obstruction checks. Observer runtime data lives outside saved objects. Observations are cached at 4 Hz; cleanup runs at 1 Hz.
- Each gunner selects its own closest eligible visible player. Another actor's sighting and hearing alone cannot grant a firing target. Existing firing arcs/range/burst behavior still applies. Native Detection and ranged LOS hooks resolve the seated observer; they do not create a moving WW brain for that body. Remote player presence targets have a body-height aim point.
- Drivers use their own detected/heard player and a world-coordinate last-known point. Engine/horn hearing uses WW rules, without acquiring vision. The MP route logic no longer gets an unseen nearest player merely from distance, including convoy pursuit. Native offline pursuit remains on its existing path.
- Dismount and reseating copy the exact departing actor's actual player knowledge, including multiple independently acquired players. Pending copies wait for normal NPC registration. Shared host contact timestamps no longer grant MP dismount awareness. Heard-only information preserves a last-known point rather than becoming visual acquisition. Already attributed hits resolve the seated victim instead of its whole vehicle; native remote hit attribution still belongs to the next step.
- Pilot decision distance considers nearby remote players. Host god mode no longer suppresses turret attacks on unrelated eligible guests.

Approximately 300 added/changed lines across Core services, NPC perception/API and Patrols targeting/crew paths, including the new `Senses.Observers.cs`. No new config, save key, prefab ID, saved component or network packet format.

## Validation and artifact

Windows PowerShell: `dotnet build WildWasteland.csproj -c Release -p:Deploy=false -v:minimal` — succeeded, **0 warnings / 0 errors**, 2026-10-18. This confirms compilation only. Runtime reflection, gameplay and networking have not been tested.

- Built DLL: `C:\Users\chura\.codex\worktrees\ww-mp-rc33\WildWasteland\bin\Release\WildWasteland.dll`.
- MD5: `AF62C98611DCA9347B80DED1C1919C68`.
- SHA256: `80099F5212430FA9ED64F7146951ACC397152EE6D033AB474E58084E42061C5D`.
- Non-live copy: `V:\Repos\Apocalypter\_staging\ww-mp-20261018\WildWasteland_0.1.36_mp_step2_AF62C986.dll`.

This DLL also contains earlier unfinished compatibility source. It is a checkpoint, not the completed install/test package. Primary main and the live game DLL were not changed.

## Manual scenarios once the full patch is ready

1. Put player A in a driver's view and nearer B behind cover/outside its initial cone. Driver pursuit must use A or a legitimately heard last-known point, without granting a gunner vision of B.
2. Show both players independently to one gunner and swap distances. It should fire on the closest eligible player within its weapon arc. Moving an acquired target behind the sight cone alone must not clear acquisition; walls/range must still block firing.
3. Give driver and passenger different sight lines. One actor noticing A must not make every actor see A, and must never reveal B.
4. Let a hidden player's engine/horn be heard, then silence it and move elsewhere. A driver may investigate the last heard point; gunners must not fire solely because the engine was heard.
5. Force dismount, then return/reseat. Each body must preserve only that actor's acquired player identities and unexpired memory, without defaulting to the host. Repeat with a turret rider.
6. Down/disconnect a targeted guest while the host stays active, then join/revive. Other players must retain separate detection. With NPC verbose logging, remote identities are labeled `player <peer>`; memory transfer uses `this crew member detected him before changing seats`.

Denis performs these checks later. No multiplayer behavior is claimed verified here.

## Next important step

Combat authority and actual remote attacker attribution: native MP client-hit/creature-damage handling, WW hurt/memory/quest consequences, duplicate damage prevention and custom projectile/melee/vehicle damage rules. Other persistence/Survival/EngineSim/model/lifecycle gaps remain listed in RESUME.md. Complete one step, build without deployment, report and pause again.
