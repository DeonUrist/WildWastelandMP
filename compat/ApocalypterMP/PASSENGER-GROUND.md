# Guest passenger exit — 2026-10-18, Testing/source only

Denis clarified the report: after riding as passenger, the guest exited and
appeared stuck below ground to the host while seeing the host/car floating.
It later recovered. Denis rejected diagnostics and requested a fix. No
diagnostic hook, periodic trace or terrain reset was added.

## Source defect and correction

Native WorldObjects.PassengerExitSpot (inspection/ApocalypterMP.decompiled.cs:
12290) casts down only four metres and accepts Draft Terrain. When no ray
hits it retains an unsupported candidate; when all three capsule checks fail
it still returns the first candidate. PassengerGetOut immediately detaches,
places the player there and restores native OnFoot physics. SafeTeleport's
0.5 s rescue excludes Draft Terrain and only acts >4 m below full terrain.
MapMagic TerrainTile.main.applyReady is set after ApplyRoutine completes.
These are source-proven unsafe exit paths; the original incident has no
position/terrain log proving which occurred. Do not claim its root cause or
runtime resolution is proven.

MultiplayerVehicles prefixes native PassengerGetOut for live guests. It uses
the native candidate, mask and capsule dimensions, requiring support, free
capsule space and, for MapMagic terrain, finished main terrain. Unsafe exits
leave the player seated, show `Waiting for a safe place to get out.` once and
retry via a 0.25 s scheduler job only while pending. Native exit runs normally
after validation. A 120 s deadline, speed >6 m/s, loss of seat, session/world
change or disconnect cancels the request. Chat, pause and Apocasetter blocking
defer retry. Native downed/death exits and host/offline behavior are retained.
Other supports such as cave floors/buildings do not require terrain identity.
No terrain rebuild, coordinate rewriting, player teleport, packet, config or
save-format change. One Core file, approximately 45 added/changed lines.

## Build and acceptance limits

No build/deploy under the shared checkout's explicit DLL-build deferral. No
new MD5, game launch, harness, commit or push. Compilation/runtime pending;
the installed DLL includes neither this change nor the AMMO-COUNT.md fix.

After the next authorized build, ride as guest into newly streamed terrain,
stop and exit. An unfinished/blocked/unsupported exit should wait in-seat,
then exit automatically when safe. Both peers should see the guest on the
same ground. Repeat on slopes, in caves, beside obstacles, near tile edges
and after origin shifts; finished-terrain exits should remain immediate.
Regress accelerating while pending, pause/chat, downing, death, car removal,
disconnect/rejoin and host passenger exit. No extra trace logging is needed.
