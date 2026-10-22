# Apparent dog one-hit kill on foot — source audit, 2026-10-20

Denis reports that a dog appeared to one-shot a client, now explicitly on foot. Full pre-hit health, dog identity and matching new host/client logs are not supplied. The available Downloads pair remains LogOutput (7)/(8), WWMP0.1.47; the current local LogOutput.log belongs to standard WW1.0.4 and cannot establish this MP incident. No production change/build/deployment follows this audit. Ultimate Tools working-tree changes remain intact.

## Confirmed source behavior

Native bite constants in `V:/Repos/Apocalypter/Sources/fsm/prefabs/`: Wild Hound and Yard Hound7 HP (`Damage/attack`, line109), Nightwalker9 (109), Grimhound15 (109), Alpha_Nightwalker25 (160), Nightwalker_Sanity4 (107). Native Player Health starts at100 (`fsm/scene1_game/Player.md:325`); Bodypart subtracts armor first and then remaining damage (36). These normal bite constants do not explain a full100-HP player dying to one correctly applied bite. NPC head damage multipliers describe damage TO creatures, not a dog multiplier against players.

The inspected native MP source (`_staging/apocapatrol-multiplayer-audit-20261011/mp-source`) sends the captured float through NetSession.SendPlayerDamage; ApplyPlayerDamage resolves only the local player's root Bodypart and WorldObjects.SendDamage sets Damage before directly firing that FSM's event. This inspected path has no dog-specific client multiplier or root-plus-head broadcast. ForwardDamagePatch sends one packet per observed native damage SendEvent. CapturePresenceDamagePatch stores PendingDamage on the target presence. This source chain alone cannot establish that a particular runtime attack generated only one send.

Native Plugin binds ReviveHealth (`Caido/VidaAlLevantarse`) with default30, configurable1–100. Survival.Revived assigns that value, rather than automatically restoring100. Four Nightwalker hits total36; two Grimhound hits total30. Actual live value/pre-hit health is unknown. Older host log8 contains four separate -9 sends before each reported revive, not evidence of a single90/100-damage bite or health measured immediately before it.

WW DogAnimations uses HitSent per dog/animation and sets it before contact; its ProcessEvent gate suppresses competing native attack starts during owned bite/recovery. DogBite's procedural fallback animates the native hit and yields to DogAnimations ownership. No shared pack-wide cooldown exists: multiple dogs can legitimately submit separate hits.

WW MultiplayerReliable includes native PlayerDamage in its ordered envelope. Receive suppresses a repeated sequence and drains consecutive buffered payloads in one loop when a missing sequence arrives. Thus delayed distinct hits can apply together and appear as one visible hit; reliable retransmission by itself is not proof of duplicate damage. There are no matching new timestamps/receive traces here to establish a burst or network stall.

## Possibilities requiring incident evidence

1. Health was below100 (including a prior revive), with enough distinct bites to exhaust it.
2. Multiple nearby dogs hit together; source deduplication is per attacker/attack, not per victim across the pack.
3. Several distinct queued damage messages arrived together after delay, while visible animation showed fewer attacks.
4. An actual duplicate attack generation, unexpected damage value, health restore/state error or concurrent damage source. None is confirmed by this source-only audit.

VehicleMeleeProtection intentionally allows on-foot targets; its engine/stationary rules cannot explain or prevent an on-foot one-hit kill. The prior disabled seated-collider reach finding does not establish this incident's cause.

Next evidence: new host and affected-client logs, incident time, exact loaded WWMP build, dog type and pre-hit HP/armor (including whether recently revived). Global VerboseLog plus NPC VerboseLog should be enabled for a controlled full-health, one-dog on-foot reproduction. Existing native host sends can reveal damage amounts/counts; absent per-hit client health/timestamps may still require a narrowly scoped diagnostic after reviewing the fresh logs. Do not guess a damage nerf or mark fixed without that evidence.
