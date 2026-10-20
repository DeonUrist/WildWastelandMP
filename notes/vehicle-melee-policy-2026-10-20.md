# Vehicle occupant animal/melee protection — 2026-10-20

Status: Testing. Denis requested animals/melee enemies can damage player drivers/passengers only when their vehicle is stationary with the engine off. This is an always-on occupant rule, independent of the collision-damage setting. On-foot players and non-player targets retain existing behavior.

## Rule

| Occupied vehicle | Animal contact / enemy melee damage |
|---|---|
| Stationary, engine not running | Allowed, subject to ordinary reach/attack rules |
| Stationary, engine running | Blocked |
| Moving, engine off | Blocked |
| Moving, engine running | Blocked |
| Reported seated, vehicle/state not yet resolved | Blocked until known |

Stationary tolerates suspension jitter: linear speed at most0.1m/s (0.36km/h) and angular speed at most0.1rad/s. Engine state means running, not merely ignition-key position. Owner-simulated cars use Rigidbody velocities and the existing EngineSim ignition service's TryRunning, falling back to NWH engine.IsRunning. Vehicles owned elsewhere use the existing native CarSnapshot Running/Velocity/AngularVelocity mirrored through MultiplayerVehicles.ApplyCarVisuals; a host's kinematic remote vehicle body must not be mistaken for stationary. Existing player snapshots now retain their existing InCar bit to distinguish unresolved seated guests from on-foot players. No protocol fields are added.

## Coverage and hook placement

New Core VehicleMeleeProtection installs through the existing shared Fsm.ProcessEvent dispatcher, independently of NPC module lifetime or optional MP availability. The cheap event/FSM gate selects native `Damage` FSM `attack` events, then checks detectedObj. Native animals and human melee enemies share that path (all51 inspected prefab Damage definitions contain the attack event). Rejecting this event prevents the attack state's damage write, sanity add and SendEvent actions from executing; it therefore prevents native ApocalypterMP CapturePresenceDamage/ForwardDamage prefixes from forwarding a forbidden hit. Merely suppressing SendEvent.OnEnter would be insufficient because other Harmony prefixes can still execute.

DogAnimations.InReach checks the same policy at start/contact. The direct spider pounce Bite path and optional DogCompanion contact also check it before damage. Ordinary native Damage Ranged/projectile paths, guns, explosions, player PvP and vehicle-part damage retain their existing rules. The requested animal rule here covers animal contact attacks (bites/pounces), not ranged creature projectiles. Native NPC targeting/aggro and navigation are not disabled; an enemy can still chase an occupant.

The separate two-file disabled-collider reach correction remains included: stationary/off permission never bypasses actual dog reach. This policy cannot by itself establish or repair the reported backward placement on downing.

## Build and stage

Windows Release/Deploy=false passed with zero warnings/errors:

`dotnet build WildWastelandMP.csproj -c Release -p:Deploy=false -p:OutputPath=bin/Release/vehicle-melee_20261020/ -v:minimal`

Exact unique stage: `V:/Repos/Apocalypter/_releases/WildWastelandMP_vehicle-melee_20261020a.dll`, built/staged MD5 **B84BCFD3285363E6F6D0AEFFE6608C6C**. Version0.1.47, handshake24 retained. Includes all current shared-tree work, including the pending speaker/shout/gender edits and dog-reach correction. No new config, save schema, packet, patch target or scheduler job. No game/harness launch, deployment, live config/save edits, commit, push or release. Current installation was standard WW without MP assets/dependency; it was not replaced.

Startup proof: `[Core] Animal/melee occupant damage requires a stationary vehicle with its engine off`.

## Denis's runtime checks

1. Update/restart all peers with matching builds. Test each table row as local host driver, guest driver, host/guest passenger, and offline driver on cars and bikes. In protected states health/armor should remain unchanged by animal contact and enemy melee; the normal reachable attacks should work after stopping and switching off.
2. Repeat while the host observes a guest-driven moving car (kinematic on host), while coasting downhill engine-off, idling stopped, and starting/stopping the engine with a nearby dog. Leave the vehicle and verify ordinary damage resumes. Repeat with EngineSim present if used.
3. Test dogs/Nightwalkers, native human melee and spider pounce contact. Test native NPC AI with the integrated NPC module off, plus optional Dog Companion if installed. Ensure combat targeting and attack cooldowns recover when targets become eligible.
4. Verify gunfire/ranged creature projectiles still behave as before, NPC-versus-NPC attacks are unaffected, vehicle parts can still be damaged, and the independent Ram damage in car collision switch retains its existing behavior.
5. Reconnect/late join and ownership handoff with seated players; unknown remote car state must not briefly grant a melee hit. Runtime/performance and the original backward-downing issue remain unverified by the agent.
