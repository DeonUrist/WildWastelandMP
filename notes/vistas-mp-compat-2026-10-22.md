# Vistas World Gen (World Amp 1.0.7) in multiplayer - 2026-10-22 - Testing

Publication update (2026-10-23): included in RC19 / 0.1.55 at Denis's request to commit and release all pending work. Combined Windows Release/Deploy=true passed with zero warnings/errors; exact DLL deployed. See [RC19 release/build evidence](release-0.1.55-RC19.md). The original staged-only/uncommitted build record below is historical; runtime checks remain pending.

Denis asked for the fix after a review (project doc `claude/wildwastelandmp-vistas-mp-compat.md`), then for every remaining gap ("100% compatible"); AI cars must ignore Vistas engine heat. Core plus one Patrols getter: `VistasWorldGen.cs`, new `VistasMultiplayer.cs`, `MultiplayerSettings.cs`, `MultiplayerHandshake.cs`, `Multiplayer.cs` (one Install line), `Patrols/Crews/Crew.cs` (`Locked` getter). Handshake 27 -> 28 (all peers update). New WW packet kinds 190/191 (Vistas weather snapshot request/data, MultiplayerSnapshot), 192/193 (terrain probe/result, unreliable). No new WW config entry or save key.

## Causes (from the decompiled Vistas source, `_tools/decompiled/VistasWorldGen`)
- Vistas cfg was never compared or shared; the manifest check only proves the same DLL. Terrain/region/road settings change the ground (Vistas rebuilds on change).
- Generation is otherwise deterministic: road network `q.A(n.A(seed, regions, Density, Width))`, generator RNGs seeded from seed/coords. Roads.Stitch/Clean are idle in the default Replace mode; Stitch only repaints splat (no heights).
- `Roads.SpawnTrack` (A/U.cs) is drawn from the local player's first position after load to the nearest place loaded so far, on a worker thread, then added to the network (re-grades/paints tiles). Different per machine and per load.
- Vistas weather (`a.y`) seeds `Random(seed ^ TickCount)`. Vistas already ships a network API for it, `a.z` (public): `a()` running, `A(bool)` follow the host (stops local rolling), `B()` snapshot, `A(byte[])` apply. Wind direction `a.g.A(float t,int seed)` and gusts `a.H.g(float t)` are functions of the local `Time.unscaledTime`.
- Dust devils (B/E.cs) spawn in front of the local camera from a TickCount RNG and push vehicles.
- `EngineHeat` is added to every VehicleController (A/U.cs) and installs an NWH power modifier / StopEngine at 140 C; wind (`a.y.H`), hill assist (`A.V.A`) and devils (`B.E.d`) push every non-kinematic vehicle body.
- Dew collectors (`a.T`) write Liquid/LiquidCapacity on every machine; on a guest copy near the player that change would be submitted as a delta edit.
- A network reset (static `A.q.A()`) bypassed `ClearWorld` (stale captured traffic roads); `RoadIdentity` read the overlay-patched serialized value.

## Changes
1. Vistas world keys join the host settings blob (id prefix `V`), applied on guests before world launch: all of `Terrain`, Regions Enabled/Scale/Blend/ExtraTypes/Landmarks, Roads Network/Density/Width/Stitch/Clean, Tiles.WeldBoost, Seams.CrossDetail, World ElevationTiers/TierStrength, Water.AltitudeDew, Vehicles EngineHeat/LooseSlopes/RoadLayers/RoadBoost/HillAssist, Weather Enabled/Gameplay/GameplayStrength/Haboob/Thunderstorms/Aurora/MeteorShower/Nebula. The rest (fog, camera, sounds/volumes, region tint/names/banner, tile visuals, weather visuals, DustDevilAmount, Debug) stays local.
2. Those keys plus Roads.SpawnTrack and Weather.DustDevils are locked for everyone while a session is active.
3. Session overlay on every peer: Roads.SpawnTrack=false, Weather.DustDevils=false (each player's cfg keeps its value; restored at session end).
4. Weather: guests call `a.z.A(true)` every 0.5 s (Vistas clears it on world reset) and apply the host's `a.z.B()` snapshot (MultiplayerSnapshot 190/191, ~1 s, late joiners request). The packet carries the host's unscaledTime; guests keep `ClockOffset` (set when >1 s off, else smoothed) and prefixes on `a.g.A(float,int)` and `a.H.g(float)` add it, so wind direction and gusts match. WW still owns dust/haboob whenever a session is active.
5. Engine heat: prefix on `EngineHeat.FixedUpdate` skips (and once unregisters the power modifier of) cars held by a locked Patrols crew, Patrols cars the player never entered, and cars another machine simulates (`Multiplayer.OwnedElsewhere`); `PickActive` postfix clears such a car from the readout. Decision cached 1 s per car.
6. Forces: transpilers on `a.y.H`, `A.V.A`, `B.E.d` replace the single `Rigidbody.isKinematic` check with `isKinematic || OwnedElsewhere` (cached 0.5 s).
7. Dew: prefix on `a.T.A(a.T+A, bool)` skips collectors owned elsewhere; the owner's values replicate.
8. Terrain check: every 10 s a guest sends a 7x7 grid (25 m) of heights from full-detail tiles in the MapMagic frame; the host samples the same points and both logs report: first match `[Vistas] Terrain matches ...` (Always), a difference seen twice in a row `[Vistas] Terrain DIFFERS ...` (Warn, 60 s per peer), skipped/pending only with VerboseLog.
9. Postfix on `A.q.A()` clears captured roads; RoadIdentity uses the effective value.

All Vistas signatures were checked against the installed `BepInEx\OtherMods\VistasWorldGen.dll` (one match each; each force loop has exactly one isKinematic call; `a.g.A(float,int)` is 29 IL bytes, above Mono's inline limit).

## Build
Cloud mcs build (quoted response file; build.sh word-splits embedded names with spaces), zero errors, no new warnings. Stage `V:\Repos\Apocalypter\_releases\WildWastelandMP_vistas-mp_20261022b.dll`, md5 `3029807f3f8d31164d4c93f6383f0c98` (supersedes 20261022a). Not deployed: the game folder has standard WW 1.0.7, MP disabled through Apocasetter, Vistas parked in `BepInEx\OtherMods`. Not committed.

## Denis's tests (host + guest, both with this DLL and Vistas 1.0.7 in plugins)
1. Startup: `[Vistas] 1.0.7 compatibility ready` and `[Vistas] Multiplayer ready: host generation settings, host weather and wind clock, terrain alignment check`; no `Compatibility installation failed`.
2. Give the guest different Vistas cfg (Terrain.HeightMeters 2000 vs 1500, Roads.Network Add vs Replace). Join. Guest log: `host world settings applied before world launch`. Within ~20 s of standing near each other both logs: `[Vistas] Terrain matches ...`; never `Terrain DIFFERS`. Quit: the guest's `apocalypter.worldamp.cfg` keeps its own values.
3. Weather: guest log `Weather: following the host.`; host `ww storm`/`ww overcast 30` -> the guest's sky changes with it within a few seconds (the guest log shows no `Weather: ... is turning ...` lines of its own). Wind sand blows the same way on both screens.
4. No `Spawn track: spawn taken` in either log during the session; no dust devils.
5. Engine heat: drive hard on the guest -> the guest's readout and seizure work; the host riding along sees no readout for that car. Patrol/raider cars never show a Vistas readout or stall from Vistas heat.
6. In session the Vistas settings above refuse changes; after leaving, local values return without errors.
7. Late join far from the host, tile borders, host save/reload, rejoin.

## Remaining by design
- Purely visual per-machine randomness stays local: individual lightning bolts, meteor streaks, mirage shimmer, ambient region/wind sound variation. They do not touch gameplay or physics.
- Dust devils are off in sessions (local camera-relative spawns cannot be shared without a new protocol).
- In a world first played solo, the spawn-track strip reverts to natural ground in MP.
