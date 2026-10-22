# Guest-side creature melee, hurt effects, combat lane, vanilla dog pace (2026-10-20, Testing)

Denis asked for these changes after the guest-downing review (project doc `claude/wildwastelandmp-guest-downing-review.md`):

- Hits must be decided on the guest and then reported to the host.
- Guests must hear and see hits.
- Dogs in multiplayer must attack at the vanilla rate.

The goal: combat looks smooth for each player. Damage a player takes or deals takes priority; host and guest may catch up on everything else.

## Cause (from the review)

- On the host, a creature's `Damage` FSM bites the guest's PlayerPresence. That is the guest's lagged copy, so bites still land after the guest has moved out of reach.
- Native `ApplyPlayerDamage` fires only the root Bodypart FSM, so guests got no hurt sound, red flash or camera kick.
- PlayerDamage and CreatureDamage shared one ordered reliable stream with the world traffic, so damage arrived late and in bursts.
- WW DogAnimations bit every 0.58 s (vanilla: attack Wait of 1 s).

## Changes

1. **`src/Core/MultiplayerCombat.Melee.cs`** (new) adds two hooks:
   - **Host:** a prefix on native `Patches.ForwardDamagePatch.Prefix`. When a tracked creature's `Damage` FSM sends Damage to a guest presence, the host sends WW kind 180 (seq, creature id, damage, reach from the `AttackRaycast` RaySensor) instead of native PlayerDamage. It also keeps a pending record. Everything else (Damage Ranged, explosions, spikes, WW bullets) stays native.
   - **Guest:** the guest handles kind 180 as follows:
     - It skips the hit when downed or protected in a vehicle (`VehicleMeleeProtection.CanHit`).
     - Otherwise it measures the distance from its own copy of the creature's `AttackRaycast` (or root + 0.25 m) to the closest point of its own collider.
     - A hit needs distance ≤ reach + 0.8 m. The slack covers the guest seeing the creature about 0.1–0.2 s late.
     - If the creature is not spawned on the guest, the host's decision is applied.
     - A hit is applied exactly like vanilla: Bodypart.Damage is set, then "Damage" goes to every enabled FSM on the player (Bodypart and DamageEffectSound or DamageEffectSound_InCar).
     - The guest answers with kind 181 (seq, hit, distance, health after) and always logs `[Multiplayer] Melee N from X: hit/dodged (...)`.
   - **Host:** logs the result in verbose mode and marks combat activity on a hit. Guest health stays guest-owned.
   - **Guest:** a postfix on native `NetSession.ApplyPlayerDamage` fires DamageEffectSound or DamageEffectSound_InCar, so the remaining native hits (NPC bullets, explosions) also give feedback.
2. **`src/Core/MultiplayerReliable.cs`:**
   - Adds a second ordered lane (frame version byte 2, retry 0.2 s) for CreatureDamage, PlayerDamage and WW kinds 180/181 (renumbered from 160/161 on 2026-10-20: those are the sleeper Pose/Remove packets, and the shared Message reader meant GuestMelee consumed 16 bytes of every host Pose packet).
   - World traffic stays on lane 0 (version 1, retry 0.35 s).
   - Dealt and received damage no longer wait behind lost world packets.
3. **`src/Core/MultiplayerHandshake.cs`:** handshake 25 → 26. Every peer needs this build.
4. **`src/Modules/NPC/Animals/DogAnimations.cs`:** in multiplayer, hostile (non-companion) dogs bite once per vanilla attack Wait (read from the Damage FSM; 1 s for every dog):
   - `NextBite` is set to at least the attack start + Wait.
   - The attack-state Wait becomes the vanilla Wait minus the time the lunge took, so hit-to-hit time equals the vanilla Wait.
   - Single player keeps the faster animated rate.

## Test (host + one guest, both on this build)

1. On the guest, let a Nightwalker pack attack you on foot. Each bite should play the hurt sound, the red flash and the camera kick. The guest log shows `Melee N from Nightwalker(Clone)…: hit (in reach …), damage -9.0, health A -> B`.
2. Sprint away from a dog on the guest. After you have clearly left, the log shows `dodged (out of reach …)` and no HP is lost.
3. Each dog's bites are at least about 1 s apart on the guest log (several dogs can still overlap).
4. On the guest, get shot by a ranged NPC. The hurt sound and flash play (native path).
5. On the host (F6 VerboseLog), check for `Creature melee on guests is checked on the guest's side` and the per-bite `Melee N: … asking the guest` and result lines with round-trip times.
6. Guest hits on creatures (given damage) should kill as before, with less delay under heavy world traffic.

Not changed: the host still decides *when* a creature attacks, and its chase/reach logic still uses the host's view of the guest. A guest the host thinks is out of reach is not attacked. Creature deaths remain host-authoritative.

## Revision b (same day, after the systems review)

- `BeforeDamageWait` subtracts the lunge time only while the dog is in a Bite/RunningBite/JumpAttack pose. A vanilla attack that slipped past `StartAttack` used a stale `AttackStart` and collapsed the Wait to 0.05 s.
- `StateDamage` now prefers native `PendingDamage` (the value `CapturePresenceDamagePatch` captured from the SetFsmFloat executed just before the SendEvent, vanilla order); the state scan is the fallback.
- A second Damage send in the same state aimed at a non-Bodypart FSM (FallDamage pattern) is ignored, natively too, so one attack cannot hit a guest twice.
- `MultiplayerCombat.BeforeNative` drops native PlayerDamage on a guest unless it came from the host (native applied any peer's PlayerDamage; guests receive other guests' broadcasts).
- `BeforeEvent` tests the event and FSM name before `Multiplayer.Active` (three reflection calls per Fsm.ProcessEvent otherwise).
- Build `_staging\WildWastelandMP_0.1.49-melee-b.dll`, md5 `d2dfa3a71cc1ceb5e5b7112d8d37d623`, deployed.
