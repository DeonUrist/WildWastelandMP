# Seated melee, blast lances and grenade throws — 2026-10-19, Testing

## Seated rotation and first-person alignment correction — 2026-10-19

Denis reports melee/gun body turning works but puts the back of the neck in the first-person view, and fists don't rotate with the view and distort. Installed prior build MD5 matched the fists handover `966D3C29229A22DC89C1DE8D932328DC`; current log confirms passenger camera switching/melee and gun spawning. No new animation exception establishes another cause.

Proven code defects: the preceding fists patch explicitly excluded fists from the whole-body turn and skipped idle-fists torso aiming. Those exclusions are removed. Fists use the existing weapon aim and turn hysteresis (95 degrees to turn,80 to return); idle arm rotations stay seated/knee-rest while punches retain the full upper-body overlay. The earlier "keeps fists seated" statement only describes that superseded build and no longer restricts weapon-style turning.

Camera fit previously applied only fixed seat-facing offsets even though the turned pose rotates about the hips and torso aiming displaces the head. Body.LateCar now fits the local first-person head to the rendered eye after pose fade and canonical CapturePose, using existing FirstPersonBodyBack, existing .075m model eye height from PlayerTraversal.TryEye, and ThirdPerson.CarShiftEye when the car body is shown. Existing base/custom seat offsets still apply afterward. This translates the local visual body; it does not change the native camera transform, physics or replicated seated root. Third-person bodies stay anchored to their seats; the usual final bone stream remains active.

One source file, about20 changed lines. Windows Release/Deploy=false zero warnings/errors; exact stage `bin/Release/WildWastelandMP_seat-turn-fit_20261019_153636.dll`, built/staged/installed MD5 `85FB6F59FB09BF2DA81E161C8CC76746`. Exact stage deployed with games closed/both disabled manifests absent. Shared-tree changes preserved/included; no config/save/protocol/base/new job/patch, game launch/harness, commit/push. No new log lines; runtime fit/performance remains Testing.

Restart host and guests. As driver and passenger, equip fists and turn while idle and punching: expect the same torso/whole-body turns as weapons, with resting arms between punches. With melee and a gun in first person, look sideways/back/up/down and cross the turn threshold repeatedly: expect the camera to remain in front of the posed head rather than viewing the back of the neck. Repeat male/female, firing/reload/swaps, holster/exit/re-enter, camera switches, host-to-guest/guest-to-host/guest-to-guest. An observer in third person should still see the actor anchored to the seat rather than the first-person visual translation. Check retained empty/fists knee-rest and melee/lance/grenade behavior.

## Fists and neutral pose follow-up — 2026-10-19

Denis confirms seated melee display works. He requests fists punches and neutral sitting with both arms on the legs when empty-handed or idle with fists, for host/client players. This supersedes wheel-hand ownership for these two neutral states, for both driver and passenger.

Cause: native fists use the `hands` weapon and Props.Kind.None. LateCarInner only watched and played strikes for Kind.Melee; `hands` was instead treated as an arbitrary held item, so the knee-rest IK owner gate also rejected it. Native source `Sources/fsm/scene1_game/PlayerCameraHolder.md:5600` onward confirms hands Animation/Attack, its native 0.35-second animation and repeated Fire flow; existing WatchAttack/PlayStrike already implements fist clip timing on foot.

Body.cs now admits hands to that existing punch path, retains the seat's lower body, blends the eleven arm/spine bones over the existing knee-rest pose during punches, yields knee IK while punching/throwing, and restores neutral hands afterward. Equipping fists clears a previous weapon's pending strike/upper weight. Empty hands and idle fists explicitly use existing RestPassengerArms for driver and passenger. Head look, active throws and confirmed melee/gun behavior retained; native first-person gameplay arms/damage are unchanged. Existing MP modes2/3 carry the final bones, so no handshake/packet/Core changes are needed. Only MP requested/changed in this follow-up; base untouched.

One source file,15 added/eight removed lines relative to task start. Windows Release/Deploy=false build passed zero warnings/errors. Exact stage `bin/Release/WildWastelandMP_seated-fists_20261019_152857.dll` deployed with games closed and both disabled manifests absent; built/staged/installed MD5 `966D3C29229A22DC89C1DE8D932328DC`. Current shared-tree work included/preserved. No config/save/asset/new job/patch, game launch/harness, commit/push. Runtime visuals/performance remain Testing.

Restart all peers. As host then guest, driver then passenger: holster everything and verify both hands rest on legs; equip fists and wait, expecting the same neutral pose; tap/hold Fire, expecting upper-body punches with seated legs; release and expect a return to rest. Observe from host, guest and second guest; repeat both sender camera modes and male/female choices. Swap from active melee/gun reload to fists and back, throw grenades, exit/re-enter, and regress confirmed melee/lance/gun behavior. Existing verbose `Strike: Punch ...` (or Punch1/Punch2 fallback) confirms the path; no new log lines.

## Original melee/throw fix

Denis reports invisible, static melee weapons in third-person vehicles locally and when observed over multiplayer. He requested upper-body swings following the first-person attack and additionally asked for visible/thrown blast lances and animated grenade throws in SP and MP. The common fix is applied to both WWMP and standard WW; their other pending changes remain intact.

## Cause and change

`PlayerBody/Body/Body.cs` enabled seated animation only for guns or an active throw. Its early return discarded melee props, and LateCar never called the existing WatchAttack/PlayStrike path. The seat pose also overwrote all three animated spine rotations, retaining only eight arm bones. Held lances took a static seat-arm branch and active throws used rifle-idle prop weights.

LateCar now observes native melee Attack state/timer changes and reuses the existing first-person-timed melee/combination upper layer. Melee props remain in the hand, and arm plus spine rotations survive the seat pose for melee and active throws. Held lances participate in the existing animated arms path and retain their world-item model; active melee/lance prop fits use the actual upper clip instead of RifleIdle. Wheel/passenger hand IK yields to these actions using its existing owner gate. Ordinary gun animation retains its eight-arm-bone behavior and the existing turned-seat behavior is unchanged.

This uses the established retargetable third-person clips synchronized to native first-person attacks; it does not copy raw native FP arm-bone curves onto the humanoid body. Native FP melee controllers have no humanoid avatar (`Sources/data/hierarchy/scene1_game/PlayerCameraHolder.txt`), so those raw clips cannot be assigned directly to this humanoid graph. First-person gameplay arms remain native. The native Attack FSM remains the source of hit timing, repeat cadence, projectile creation, ammo use and lance removal; no new damage/throw authority or projectile creation is introduced.

Source evidence: `Sources/fsm/scene1_game/PlayerCameraHolder.md`, melee Animation/Attack around lines6119–6180; blastlance Attack around4597–4660; grenade Animation/Attack around2147–2218. The existing WatchThrow/PlayThrow clock handles lance and grenade starts and continues across native weapon holstering/release. Spine retention now preserves the full upper-body contribution of those throws. Actual fit and release visuals still require Denis's test.

WWMP handshake23 already sends the fitted held-item identity/transform and final bones for driver/passenger pose modes2/3. Correcting the local pose therefore reaches host-to-guest, guest-to-host and guest-to-guest observers through the existing stream; no protocol/Core changes are required. Network damage/explosion behavior is outside this presentation fix and remains native/host-authoritative as before.

One source file per repo, **17 additions / nine removals** against task-start copies. No config/save/asset changes, new scheduler job or Harmony target, Core/other-module changes, game launch/harness, commit/push/release. Source/runtime status remains Testing.

## Build and deployment

Both Windows Release builds passed with **zero warnings/errors**, using `-p:Deploy=false` and separate output directories `bin/Release/seated-melee_20261019/`.

| Repo | Unique stage | MD5 | Installed |
| --- | --- | --- | --- |
| WWMP | `bin/Release/WildWastelandMP_seated-melee_20261019_150529.dll` | `5754C7A6D731F459324A9F741A4C7A46` | Yes; built/staged/installed hashes match. Games closed, both WW/WWMP disabled manifests absent. |
| Standard WW | `bin/Release/WildWasteland_seated-melee_20261019_150529.dll` | `451E447FE90ADC30320F12C2785D6088` | No, per standing no-base-deployment policy. |

Builds include their current shared working trees. The combined MP build also includes the previously staged upright vehicle-load fix. All multiplayer peers must restart with matching DLLs. No gameplay/performance acceptance is implied by compilation or deployment.

## Denis's checks

1. Restart host and both guests with the deployed build. In third person as driver and passenger, equip machete, knife/shiv and wrench; tap and hold Fire. Expect a visible held weapon and one upper-body swing per native attack, with continued swings while holding and no wheel/knee IK overriding the action. Repeat first/third-person switching, aim left/right/up/down and male/female/native character choices. Holster, swap to a gun, leave/re-enter the car and check normal hands/gun behavior returns.
2. Equip both blast-lance variants while seated: expect a visible lance before throwing, the right-hand throw motion, disappearance when native release consumes it, and the native projectile/explosion. Throw a grenade while unarmed and while holding melee/gun: expect the upper-body throw to continue across temporary holstering, with native grenade consumption/projectile and normal held-item recovery afterward. Try successive throws; each native throw should animate once.
3. Repeat with host acting and guest observing, guest acting and host observing, then guest acting and the other guest observing. Expect matching held models, swings, torso motion and release without a stuck weapon. Repeat offline in WWMP and, when Denis installs the staged standalone build, in standard WW.
4. Regress gun aim/fire/reload, wheel hands, passenger knee-rest hands, seat transitions and on-foot melee/throws. No new logging is added. Existing VerboseLog-enabled `Melee: first-person swing ...`, `Strike: ...`, `Throw: ...` and prop/view messages can confirm path execution; their absence with logging off is expected. Visual fit, synchronization and performance remain unverified until these checks.
