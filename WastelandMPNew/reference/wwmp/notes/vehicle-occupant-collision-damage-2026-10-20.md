# Player occupant collision damage — 2026-10-20

Status: Testing. Denis requested vehicle collision damage disabled by default for player drivers and passengers after reports of riding normally before downing far behind. The observer-client log did not identify the damage source or establish the displacement cause. This change closes a collision-damage policy bypass; it is not proof of that incident's cause.

## Source and behavior

- `src/Modules/Patrols/PatrolsSettings.cs` already binds `Ram damage in car` with default false. Explicit existing values and legacy migration are retained. Its description now includes native crashes as well as AI rams. The existing host-authoritative settings synchronization supplies this same entry to guests.
- `src/Modules/Patrols/Crews/Ram.cs`, `Hit`, already returns before damage when kind=2 (vehicle occupant) and that switch is false. `Ram.Network.cs`, `RemoteKind`, classifies remote drivers/passengers by their reported vehicle. This handles WW AI rams for local and remote occupants.
- Native prefab `V:/Repos/Apocalypter/Sources/fsm/prefabs/Crossbreed.md`, CrashDamage/damagePlayer, finds the Player under DriveTrigger/sitPos, writes Bodypart.Damage, then sends Damage. Its subsequent nextPart/damagePart states damage vehicle parts independently. This native driver path previously ignored the WW setting. The other inspected native vehicle CrashDamage FSMs use the same pattern.
- New `src/Core/VehicleCollisionDamage.cs` subscribes to the existing shared SendEvent.OnEnter dispatcher. Plugin installs it after all config is bound, before module startup. Core ownership keeps protection active on guests where Patrols simulation is stopped and when Patrols is off.
- With the switch false, skip only CrashDamage's Damage event with an explicit GameObject/GameObjectFSM target equal to the local player or a child. Finish the intercepted non-everyFrame action so the vehicle FSM proceeds normally. Unrelated events, car-part damage, NPC damage, on-foot rams, shots, melee and explosions retain their existing behavior. No scene scan, timer, new Harmony target or network message is added.
- Native ApocalypterMP ForwardDamagePatch also prefixes SendEvent.OnEnter. Its guest forwarding resolves tracked creatures; this native local Player target is not one. The new guard does not rely on preventing another Harmony prefix from running.

## Build and staging

Windows command: `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=false -p:OutputPath=bin/Release/occupant-collision_20261020/ -v:minimal`.

Result: zero warnings/errors. Built and uniquely staged DLL MD5: `255A5D02FF82E46E79AF6CC5E7321685`.

Stage: `V:/Repos/Apocalypter/_releases/WildWastelandMP_occupant-collision_20261020a.dll`.

Plugin version remains 0.1.45, handshake 24. No config/save format changes. Current game installation contains standard WW, no MP asset folder/dependency, and a running Apocalypter process (45628); no deployment or replacement of that setup. No game/harness launch, live config/save edits, commit, push or release.

## Denis's runtime checks

1. Update/restart host and guests. With host `Ram damage in car` off, test driver and passenger roles in cars and bikes: wall/terrain collision and a hostile car ramming them. Player health should remain unchanged from these collisions; car parts may take damage. Repeat with Patrols simulation off for the native crash path.
2. Repeat host and guest driver native crashes with the switch on to confirm opting in restores the native damage path. Switch back off. Existing profiles with an explicit true value retain that opt-in.
3. Confirm on-foot ram settings, NPC bumper damage and ordinary enemy shooting still work. The occupant switch does not grant general invulnerability.
4. Global VerboseLog optionally records `[Core] Blocked vehicle collision damage to player from <vehicle>` when the new native guard rejects an event. Startup records `[Core] Vehicle occupant collision damage follows Ram damage in car (default off)`.
5. If the backward downing recurs, collect host and affected-client logs together; the prior observer log cannot establish where the affected player's authoritative position/damage diverged.
