# Verification and acceptance

## Verification performed for this handoff

Windows .NET SDK10.0.402; C#7.3; references from the installed game's managed assemblies. The kernel and separate Steam adapter compile together using:

```powershell
dotnet build WastelandMPNew/steam/WastelandMPNew.Steam.csproj -c Release -p:Deploy=false -v:minimal
```

Final result and exact DLL hashes are recorded in [BUILD-EVIDENCE.json](BUILD-EVIDENCE.json). Compilation confirms API/type compatibility with the installed reference assemblies, including Steamworks.NET. It does not execute the SDK, exchange packets, validate gameplay or measure performance.

No game, harness, test project or simulation was run. No active plugin/config/save was changed, no deployment occurred, and no commit/push/tag/release was performed. This is a staging source module outside the production compile glob. Existing root dirty files were preserved; the root AGENTS entry only records the new handoff location and a WW2 amendment lesson.

## Before enabling multiplayer in WW2

These checks belong to the later integration task. Under the repository's build-only policy Denis performs game checks. Do not label the supplied kernel or archived historical test reports as runtime Verified.

| Gate | Expected behavior |
|---|---|
| Offline baseline | WW2 still loads/saves/plays without any MP or optional-mod dependency. No listener/lobby before explicit hosting. |
| Fresh host launch | Selected save identity is pinned before scene entry; actual seed/world policy retained. Cancellation/stale callback never opens a transport. |
| Required build/schema mismatch | Reject before guest world generation with named cause; no partial avatar or changed local settings. |
| Settings | Guest with different terrain/world values receives host overlay before generation; own config bytes stay unchanged after leave and ordinary settings save. |
| UDP setup | Unique per-peer session keys; wrong key/old epoch/reflected direction/replayed data cannot cause world mutation. No claim of confidentiality. Public discovery requires the missing key-exchange design. |
| Ordered traffic | Loss, duplicates and reordering cause at most one ordered application; send/backpressure retains work. Full queues fail/defer explicitly. Combat progresses during large world join. |
| Budget/fairness | Named jobs report counts/bytes/queue occupancy/CPU; rotating per-peer channel phases and global peer scheduling remain responsive under sustained incoming traffic. |
| Snapshot/admission | Missing/corrupt/conflicting chunks never apply. Final receipt alone does not make Ready. Stale attempt tokens cannot finish a replacement join. |
| Authority handoff | Former owner revision cannot apply forces/collectors/tool/part edits. Host-driver/guest-passenger and guest-driver/host-passenger agree on latest car pose and seats. |
| Resource races | Host+two guests use same ammo/food/water/money: no duplicate credit. Retry/disconnect/freeze/timeout has explicit result. Failed host commit blocks publication. Pour/refill and third-party operations need their own completed adapters. |
| Save timeout | One total30s budget, named yellow warning, last confirmed host state only; queues retained. Actual write/terrain/integrity failure remains blocked after later successful writes. |
| Save/reload | World, all-player inventory/vitals/position/anchors, quest and optional tool state belong to the same snapshot generation and full backups. Older snapshot never receives newer checkpoint state silently. |
| Sleeper/respawn | Retain physical body and inventory through interrupted join. Carry moves reconnect location; chosen anchor unchanged. Host crash/restart follows the selected snapshot. No unsupported-terrain gravity or blocked standing placement. |
| Combat | One committed hurt result; damage/revive/restore order retained; disabled collider reach rejected; seated melee policy and crash protection apply per target; no backward teleport claim without evidence. |
| Merchant/cargo | Paid/hostility release distinguished, friendly repricing correct, no revisit deletion, no duplicated stock/cargo, first-spawn audit scoped to the physics area. |
| Steam two accounts | Friends lobby/invite and command-line join; host metadata mismatch/owner departure/full lobby/unavailable Steam/relay delay; cancel during create/join; repeated stop/start; SDK message handles always released. |
| Vistas | Different local generation preferences converge before launch; source follow/weather/wind clock restored after reset; terrain probes near/far/borders; no SpawnTrack/devils in session; only owner forces/dew/heat; AI cars excluded. |
| Optional mods | Missing mod, newer supported signatures and missing required signatures behave distinctly. Test Vistas+EngineSim ownership, Ultimate Tools save/late join, logical extra inventory slots, Dog Companion poses, motorcycle physics and local camera/HUD. |
| Teardown | Departure revokes admission before caches; delayed packets do not recreate avatars/markers. No save occurs just because guest disconnects. Steam lobby owner change never silently migrates world authority. |
| Performance acceptance | Compare offline and host+1/2 guests across join/driving/AI/fight/save/weather/tile seams; capture average and p99 CPU/frame, allocations, stalls, bytes/sec and queue depth. Fixed limits are not measured gains. |

The later integration should add meaningful protocol-level automated tests if Denis authorizes changing the current build-only verification policy. No test scaffolding is bundled or represented as having passed here.
