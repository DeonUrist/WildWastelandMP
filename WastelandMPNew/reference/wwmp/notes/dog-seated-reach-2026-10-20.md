# Dogs and seated remote-player reach — 2026-10-20

Status: Testing. Denis reports the riding/downing-far-behind incident recurred and seems associated with camps containing dogs/quadrupeds. Global and NPC VerboseLog were enabled; new incident logs have not yet been supplied. This source finding identifies a real false-reach path, not a runtime attribution of the entire incident or an established explanation for backward placement on downing.

## Supplied log cross-check (later in the same session)

Denis supplied `C:/Downloads/LogOutput (7).log` (23,368 bytes, guest) and `C:/Downloads/LogOutput (8).log` (550,779 bytes, host). Both load WWMP0.1.47. Guest role/exact-mod acceptance appears in log7 lines120–122; host role and Steam hosting in log8 lines141–147. Source logs remain outside the repository and release package.

- Log8 lines865–869: Nightwalker533/532 acquire peer1 at37/33m. Lines870/871/875/876: four reliable native damage sends, `Enemies: a creature hit THGSnek (-9)`. Line1123: host revives THGSnek.
- Log8 lines1401–1419: Nightwalker533 acquires peer1 at44m, then native damage send at1405; Nightwalker530 at31m followed by sends1411/1412; Nightwalker531 at27m followed by send1419. Line1965: host revives THGSnek again. Eight logged sends in total, nominal72 damage across both episodes, not a logged initial/final health measurement.
- `Sources/fsm/prefabs/Nightwalker.md`, Damage/attack (line109), sets Bodypart.Damage=-9. The native SendPlayerDamage log identifies the receiving peer and amount but not the attacking creature, collider state or distance at that exact hit. Target-acquisition distances nearby in log order therefore strongly support the disabled-collider false-reach explanation, but are not per-hit measured ranges or definitive attacker IDs.
- Log8 line860 explicitly shows native driver crash damage from Junker blocked. The later `Ram: car hit -> Nightwalker...` lines describe damage to the animals, not to THGSnek. These are separate damage directions.
- Client log7 lacks individual incoming damage, health and seat/downing-position transitions, so the exact backward jump remains unresolved. Its Invalid player pose offset warning (line222), and host's earlier warning (line272), reject render-pose offsets; those messages alone do not prove a player physics/seat teleport. Guest bone warnings211–219 align with the host reporting peer1 carrying Grimhound_Dead511 (line368) and later releasing it (line1408); they do not prove a stranded player body.

Conclusion: host-sent creature damage and two revives are runtime-confirmed. Attribution to the dog false-reach path is strongly supported by the Nightwalker targets/damage signature and source chain below. The backward-placement mechanism and exact affected-player seat transitions remain unproven. The current two-file guard is still a staged/unpublished change; these supplied logs do not validate it.

## Source chain

- Native ApocalypterMP `PlayerAvatar.Update` calls `presenceBody.SetSolid(!current.InCar && !current.Downed && ...)`. `PlayerPresence.SetSolid` sets its own Collider.enabled accordingly. Seated remote players therefore keep a PlayerPresence object but have its collider disabled. In this checkout, `Multiplayer.PresenceAdded`/`RemoteAt`/`PlayerFor` expose that body to NPC targeting.
- `DogAnimations.TargetCollider` caches target.GetComponent<Collider>() without an enabled-state check. `ReachPoint` previously called that collider's ClosestPoint(origin), where origin is the dog's attack origin, regardless of enabled state. Unity documents that a disabled collider returns the input point: [Collider.ClosestPoint](https://docs.unity3d.com/ja/2022.1/ScriptReference/Collider.ClosestPoint.html). The project's2020.3 API page omits that explanatory note; no engine runtime/harness was launched to independently verify native behavior in this game.
- Therefore `InReach` can calculate `point-origin=0`, passing distance, height and facing gates. The line-of-reach becomes degenerate too, so it is no longer a meaningful obstruction test. `TryAttack` can start a bite, and the animation's contact phase calls InReach again before setting Damage.detectedObj and emitting attack. Native CapturePresenceDamagePatch plus ForwardDamagePatch forward that hit to the remote peer without a second distance check. This is a plausible source of continuing bites after driving away, provided the dog retains that combat target and its damage FSM is ready.
- `DogBite.Tick`, used without the animated dog owner, had the same unchecked ClosestPoint call in its stop/chase calculation. That code controls posture/movement; it does not itself generate distant damage.

## Minimal correction

`DogAnimations.ReachPoint` and `DogBite.Tick` now query ClosestPoint only for an enabled collider on an active hierarchy. Otherwise they use the target's actual Transform.position, matching the existing no-collider fallback. Enabled-collider surface distance, attack cadence, target selection, range, native MP hit forwarding and seat-collider ownership remain unchanged. No new scan, job, hook, packet, config or save field. The per-frame point cache is unchanged; a disable during an already-computed frame may retain that frame's existing point, then the next frame recomputes.

Scope is these two dog paths. The spider Pounce path also contains a cached-collider ClosestPoint call; its attack/target selection must be audited separately before attributing a spider incident or broadening this fix. No general animal invulnerability or vehicle collision change is introduced.

Windows Release/Deploy=false build passed with zero warnings/errors:

`dotnet build WildWastelandMP.csproj -c Release -p:Deploy=false -p:OutputPath=bin/Release/dog-seat-reach_20261020/ -v:minimal`

Unique stage: `V:/Repos/Apocalypter/_releases/WildWastelandMP_dog-seat-reach_20261020a.dll`, built/staged MD5 **A778E0FD0C381B59B676C60CB283FC27**. Version remains0.1.47/handshake24. The shared checkout also contains pre-existing speaker/shout/gender edits in Services, NPC and PlayerBody; they were preserved and are included in the compiled DLL. This stage is not an isolated release of only the dog fix. No deployment/game/harness/live config/save edits/commit/push/release.

## Denis's checks / evidence still needed

1. Preserve the recurrence's host, affected player and separate guest-driver logs, including the exact build/version, roles and boarding-to-downing period. Note the dog type/camp, approximate incident time and how far the vehicle travelled. These can distinguish new remote bite damage from collision damage and stale authoritative position/seat state.
2. With matching test DLLs, have a hostile animated dog acquire a guest on foot, board as passenger and drive beyond bite range while another peer observes. Repeat as guest driver and with the host driving. A dog remaining at camp must not damage that player from afar. Check the target exits and can again be bitten when physically nearby.
3. Repeat without dog animation assets/with its existing animation switch off to check chase/halt behavior, and test on-foot bites against host/guest/NPC targets. Preserve normal nearby attacks rather than assuming every seated player must be invulnerable.
4. If a backward downing still happens after the false-reach correction, compare authoritative presence position, car/seat and downing transitions. The original observer log and this source finding do not prove those states diverged.
