# Recent improvements to carry from WWMP into singleplayer WW

Documentation and source audit only, refreshed 2026-10-19 at approximately 14:58 Europe/Bucharest (11:58 UTC). This is a handover for a future port, not a claim that the missing features have been implemented or verified in standard Wild Wasteland.

This refresh adds loading-screen motion/rotation, local head look, the latest native haze bypass and Debug placement, and the new seated-save load correction. The earlier storm-density fade and WORLD fog-settings advice are superseded. It also preserves the actual installed loading artwork/tips and current persisted settings, rather than relying on source defaults or a future installed folder staying unchanged.

- [Current settings, complete main-root values and client differences](SP-PORT-CURRENT-SETTINGS-2026-10-19.md)
- [All 18 loading screens, exact tips and port/packaging instructions](SP-PORT-LOADING-SCREENS-2026-10-19.md)
- [Captured assets/configs and SHA-256 manifests](sp-port-reference-2026-10-19/README.md)

## Compared snapshots

| Item | Snapshot |
| --- | --- |
| Source | `V:/Repos/Apocalypter/WildWastelandMP`, HEAD `6faf77b213407df1400001c728214ed075a287c1` **plus its current modified and untracked source/assets** |
| Destination | `V:/Repos/Apocalypter/WildWasteland`, HEAD `7208ca0a103b52f34e08ca5a33d5fb6e6de1784e` (0.1.40 RC37) **plus its current working tree** |
| Recent-work boundary | The October 18–19 fork work after shared `91bff52`, including pending save/inventory/crawl changes. Earlier shared RC33-and-before work is already inherited by both repos. |
| Source build reference | Follow-up deployed stage: `WildWastelandMP_seated-melee_20261019_150529.dll`, MD5 `5754C7A6D731F459324A9F741A4C7A46`, handshake23. Includes the previously staged vehicle-load correction and new A11 seated melee/throw presentation. SP common A11 fix is built/staged as `WildWasteland_seated-melee_20261019_150529.dll`, MD5 `451E447FE90ADC30320F12C2785D6088`, not deployed per standing policy. Runtime/visual/performance acceptance remains Testing. |
| Destination dirty code | `src/Modules/Survival/StewPot/Factory.cs`; its submesh-material change is already present but uncommitted. Destination also has documentation changes. |
| Supporting inventory | [SP-PORT-FILE-AUDIT-2026-10-19.tsv](SP-PORT-FILE-AUDIT-2026-10-19.tsv): 206 rows—114 differing files, 84 MP-only files and eight equal comparison anchors, with per-file port classification and SHA-256 for both working trees. Text comparison normalizes CRLF/LF; hashes record exact file bytes. There were no SP-only `src/` paths at capture. |
| Installed reference capture | 53 exact files / 7,366,399 bytes: all 18 numbered PNGs, BG_MAIN menu image, screens.json, nine config files from three roots and existing PlayerBody/CleanHUD profiles. [Manifest](sp-port-reference-2026-10-19/file-manifest.tsv) records original paths, UTC modification times and hashes. Config copies use `.cfg.txt` to survive the repo's `*.cfg` ignore rule. |

Important: most of the newest source is **not committed**. Checking out/cherry-picking source HEAD alone omits the save library, inventory drops and several fixes. Recheck both working trees and the inventory before implementing. Whole-file differences often consist mostly of MP code; the feature boundaries below take precedence over copying a file.

## Missing SP work: port map

Paths starting `src/` are relative to the **source repo**; shortened `Core/...` paths mean `src/Core/...`, and module names such as `PlayerBody/...` mean `src/Modules/PlayerBody/...`. Same-named destination files should be edited selectively. A source name containing Host or Multiplayer does not make all its behavior MP-only. In particular, the save engine already has an offline-WWMP path, and the inventory drop recipe is reusable locally, although its current entry point deliberately requires an MP world.

| ID | Improvement | Current SP status | Source entry points / scope |
| --- | --- | --- | --- |
| S01 | Compact named save/load library and SYSTEM settings | Missing | `src/Core/UI/HostSavePage.cs`, `Core/UI/ToolWindow.cs`, `Core/UI/CorePages.cs`, `Core/UI/ToolPage.cs`, `Modules/CleanHUD/Menu.cs`; use the singleplayer portions of `HostSaveSystem.cs`. |
| S02 | Durable offline saves, backups, validation, publication and rollback | Missing | `Core/SinglePlayerSaving.cs`, `HostSaveFiles.cs`, `HostSaveSystem.cs`, `SaveBus.cs`, `Log.cs`, native menu/load integration. Adapt the standalone launch/slot adapters; do not bring in MP recovery/barriers. |
| S03 | Separate autosaves, visible countdown and combat deferral | Missing | `Core/HostSaveCombat.cs`, `HostSaveWarning.cs`, `HostSaveSystem.cs`; `Core/Services.cs`, `Modules/Guns/GunsApi.cs`, `NPC/Detection/Senses.cs`, `Patrols/PatrolsModule.cs`, `CleanHUD/Hud.cs`, `CleanHUD/GlyphText.cs`. |
| S04 | Saving while in a vehicle, including correct freeze order | Missing from the new save-engine behavior | Offline branches of `SinglePlayerSaving.cs` / `HostSaveSystem.cs`; scoped native InCar/menu activation handler currently in `Core/MultiplayerSaving.cs`. Extract the local menu/save behavior only. |
| S05 | Apocasaver takeover with stable hinge/pose keys and persistent world history | Missing | `Core/ApocasaverExclusion.cs`, `HostSaveCompatibility.cs`, `HostWorldHistory.cs`, `Patches.cs`, `Plugin.cs`; dependent on S02. |
| S06 | Optional conservative save cleanup | Missing | `Core/HostSaveCleanup.cs`, save-engine/UI hooks, existing Patrols cleanup settings. Keep the native offline item-list path and adapt custody tracking. |
| S07 | Complete companion files and save-and-quit sidecar persistence | Missing corrections | `Core/HostSaveFiles.cs`, `Modules/CleanHUD/LocationMarkers.cs`, `Modules/POI/PoiModule.cs`, plus save-failure/completion hooks in `Patrols/Persistence/Persistence.cs`, `CleanHUD/MapFog.cs`, `Quests/Plugin.cs`. |
| S08 | Inventory quantity drops: ammo, grenades, batteries, Nuts, Bandages, First Aid | Missing | `Modules/CleanHUD/InventoryStyle.cs`, quantity/input helpers in `InventoryStyle.Multiplayer.cs`, local creation/count/split recipe in `Core/MultiplayerInventory.cs`; native prefab/save registry support already exists in SP. |
| S09 | Eight local character choices, matching portrait tiles and TAB portraits | Missing | `PlayerBody/Body/CharacterPortraits.cs`, `MultiplayerModels.cs`, `Model.cs`, `Body.cs`, `InventoryModel.cs`, `BodyRunner.cs`, `PlayerBodyModule.cs`, `PlayerBodyPages.cs`, `Core/UI/CorePages.cs`, eight `PlayerBody/Embedded/CharacterPortraits/*.png`; character-asset service in `Core/Services.cs` and `NPC/Women/`. |
| S10 | Crawl reentrancy/disabled-Movement guards | Missing follow-up to existing SP crawl | `Modules/PlayerBody/Animation/Crawling.cs`: Stance calls Holster, not Tick; require active/enabled initialized Movement and compare actual native state before downed stance forcing. |
| S11 | Remove native Azure distance whitening while Weather runs | Missing, visually unverified correction; replaces ineffective density fade | `Modules/Weather/WeatherSky.cs`: temporarily null gameplay-camera `fogScatteringMaterial` to use native unfiltered blit; finalizer restores the exact borrowed material. See [latest haze handover](weather-distance-haze.md). |
| S12 | Per-arm pose offsets on weighted ModAPI characters | Missing | `Modules/PlayerBody/Animation/ModCharacter.cs`: `ArmPoseApply()` after `AimLiftApply()`. Keep the unrelated rig-network registration out. |
| S13 | Compass marker labels and refresh when a pooled entry changes | Missing | `Modules/CleanHUD/Hud.cs`: marker distance text includes the existing style label and invalidates on entry identity changes. Preserve existing marker visibility/ambiguity choices. |
| S14 | Optional mod recommendations default off | Missing | `Core/ReplacementGuard.cs`: `[Debug] Recommended mods` default false; warnings about a required restart remain independent. |
| S15 | All map-fog settings grouped in Debug | Missing latest UI follow-up | `Core/UI/CorePages.cs`: remove map-fog rows from WORLD/host setup; group fog enabled/radius/edge/opacity under Debug > CleanHUD > Map fog of war and omit generic duplicate rows. Existing keys/defaults/Debug visibility remain. Exclude Shared vision and host/guest authority. |
| S16 | Crossbow landed-bolt robustness | Missing small shared corrections | `Modules/Guns/Tracers/Bolts.cs`: retry bolt-model caching when needed; `Tracers.cs`: move unparented landed bolt visuals during origin shifts. Exclude all Network* calls/state. |
| S17 | Loading artwork drift, ten-second image/tip rotation and smoother motion | Missing | `Modules/CleanHUD/LoadingScreen.cs`: local Begin/UpdateBackground/ShowScreen changes; exclude MultiplayerSaving.Frozen and server-save adapter. Capture/ship all installed assets from the linked loading-screen reference. |
| S18 | Local seated neck/head look and residual foot/NPC head pitch | Missing common hunks from the seated-aim fix | `PlayerBody/Body/Body.cs`, `Animation/Loco.cs`, `Animation/ModCharacter.cs`; extract local residual look only. Fitted-prop pose packets, receiver item selection, network first-person capture and NPC manifest refresh are MP-only. |
| S19 | Loading a seated save upright beside a stopped vehicle | Missing from SP; included in the combined deployed MP melee follow-up | New `Core/VehicleLoadPlacement.cs`, `Core/Hooks.cs` WaitUpdate dispatcher, `Patrols/Persistence/PatrolLoadPreparation.cs`; current install/load hooks in `Core/Multiplayer.cs`. Extract offline native restore behavior and adapt main-terrain/standing-clearance helpers without MP dependencies. |

### S01–S02: save library and offline save transaction

Port the complete behavior together, not just the row renderer:

- Unlimited named snapshots with unique internal identities, separate world/save names, search, five screenshot rows per page, preview/details, Manual/Autosave/Backups views, explicit overwrite/delete/restore/load confirmation and opening the backup folder. Keep the latest subtitle removal: no `Your worlds, ready when you are.` line. An SP-only library needs no multiplayer/player-count column or hosting setup.
- Screenshot/name/date/world/distance metadata. The native `DistanceTravelled.distance` is **distance from the starting area**, refreshed periodically, not a cumulative odometer. Display unknown when old metadata lacks a field; never invent values or reuse a stale screenshot when capture is disabled/unavailable.
- Fresh offline worlds receive separate working identities; named saves are published snapshots rather than redirecting native serialization into a loaded source midway through a run. Legacy SaveGame1–10 remain readable. Continue chooses the correct newest usable SP save. Confirmed in-session loading returns through the native title loader.
- Freeze time and block input **before** capture/transaction/serialization. Use the native serializer and activate save-participating objects as native saving does. Complete only after native saving, all required sidecar callbacks and validation finish. Provide a native-font saving fallback when CleanHUD presentation is unavailable.
- Back up the full participant set; write durable staged files/manifests, validate required ES3 content and the transaction marker, publish the undo journal last, and recover an interrupted transaction. Roll back bytes **and timestamps**, so Continue does not accidentally choose the wrong source. Preserve loaded manual sources outside explicit transactions despite native dirty-cache flushing.
- Preserve missing/extra companion-file state when restoring: deleting only the ES3 or restoring only old files can leave newer sidecars attached. User-deleted snapshots and unresolved recovery journals are retained; automatic retention must not remove them.
- Module serialization failures currently swallowed by catch/log paths must reach the transaction. Select the `ReportFailure`, cancellation and completed-sidecar hooks, including quest unreadable-format/cache failures, fog failures and Patrols cache/disk failures. `Log.Error` currently also calls `HostSaveSystem.Fail`; review that coupling with the standalone transaction rather than replacing the entire logging service blindly.

**Standalone blockers to remove:** `SinglePlayerSaving.Install()` currently obtains GameLauncher through `Multiplayer.Type/MultiplayerAccess`, and the save library/catalogue uses `MultiplayerHosting`/WorldStore adapters. Even the offline path is initialized inside the MP bridge. A standard-WW port must bind native game/FSM loading, paths and slot validation from standard Core, initialize without ApocalypterMP installed, and supply a normal Core owner for jobs/hooks. Copying these files unchanged will not give independent SP support.

Source `MP_sp-live-*`, `MP_sp-auto-*`, `MP_save-*`, `WildWastelandMP-Saves` and `WildWastelandMP.SaveCommit` are fork-specific identities/metadata. Resolve the standalone namespace and backward-reading policy explicitly; preserve existing native/module save keys and user paths. Do not automatically move/import launcher saves, rewrite live saves, or relabel an MP world as SP. Network player snapshots, player checkpoint files and owner barriers are not required for standalone saving.

### S03–S04: autosave safety and vehicles

Keep separate autosave identities and retention; autosaves never overwrite named manual snapshots. Source defaults are 10 minutes, five autosaves per lineage, five backups per save, thumbnails on and autosave while driving off. Warning seconds is the existing 30–120 range with default/minimum 30. Reuse existing keys if they exist in the destination, preserving user values.

Countdown ticks once per second beneath the actual compass/fallback, always in the native game font, with 30/15-second notices and a three-second `The game will save now` phase. Local fight detection includes native NPC/animal detection, attack/damage/gunfire and occupied vehicle-combat targets. Search ghosts and empty patrols must not postpone saves forever. After combat, require ten quiet real seconds and restart the full warning. Recheck immediately before serialization; late combat cancels a scheduled save without entering a failed transaction. Manual/exit saving keeps its explicit semantics. Strip guest attack observers, packets167/168, host epoch/stale-network handling and broadcast notices.

Vehicle saving retains native occupied-car serialization and does not force the local player out for manual save/save-and-quit. The driving option applies to offline autosaves too. Extract only the InCar Save control visibility/hint and local routing from `MultiplayerSaving.cs`; its Frozen car snapshot sampler and reliable car-owner flush are MP-only. Confirm freeze/input-block order on the standalone path. Preserve normal get-out interaction outside this exact save UI scope.

**S19 is the corresponding load correction.** Native ES3 restores the player's seat parent but does not reconstruct the live InCar/Drive ownership lifecycle; loaded `PlayerInCar` also selects a crawl fallback. During native loading, temporarily detach the saved seated player while kinematic/noncolliding, suppress that loaded seated flag in memory, and hold the final terrain wait until main terrain and a clear supported standing capsule beside the saved vehicle exist. Park the restored vehicle (zero throttle, handbrake, stop engine through the ignition service/native fallback, zero car/part velocities), then enter OnFoot/Standing upright, restore collision handling and zero player velocity before releasing control. Do not rewrite the saved snapshot or force an exit at save time. Review the disabled/destroyed-load cleanup and blocked-clearance retry behavior in the standalone lifecycle.

`VehicleLoadPlacement` is currently installed through MP Core and calls `MultiplayerRespawn.Surface`/`Multiplayer.LocalTargetable`; replace those adapters with independent local terrain/stance checks. Keep MapMagic main TerrainCollider/applyReady requirements and standing clearance, rather than accepting a draft terrain/floor as readiness. Its shared `Hooks.WaitUpdate` dispatcher also replaces PatrolLoadPreparation's previous private Wait patch: port both registrations/cleanup together, preserving the existing Patrols cache/AI load gate. Native guest wake/respawn uses the same upright helper in MP but is excluded from the standalone port. The latest source task built successfully; actual loading and save-integrity acceptance is pending.

### S05–S07: history, cleanup and companion persistence

If standalone WW takes ownership of autosaving, suppress standalone **Apocasaver**, not **Apocasetter**. Preserve external DLLs, data/settings and optional compatibility APIs. Source soft dependency, runner/callback suppression and removal of that exact foreign patch owner must be installed in the correct order. Existing SP Apocasaver support must continue until its replacements are installed; do not disable it as an isolated first step.

Retain Apocasaver-compatible stable hinge rotation keys and cached hand-pose metadata, and world-history capture before terrain pooling/template reset. This prevents distant static-location changes disappearing after unload/reload. `HostWorldHistory`'s capture cache and ES3 access use generated reflection helpers; provide an independent Core equivalent or adapt those accesses without the MP assembly.

Conservative cleanup is opt-in/default off: abandoned non-resource Zombie_Runner remains and exhausted disposable ammo/battery packs only, after continuous gameplay beyond at least 1,000 m for at least ten minutes. Preserve player-owned/touched items, resource bodies, equipped/parented objects, live actors, saleable packs and native respawn-relevant objects. Source uses the native ArrayList_Items for offline observation but still resolves MP tracked-state delegates at install; remove that dependency. Keep existing Patrols cleanup separately exposed and do not import multiplayer sleeper/anchor/peer-ownership retention wholesale.

Companion coverage includes native world metadata/screenshots, NPC senses, Patrols `.bin` plus backup, POI `.poi.txt`, personal fog `.fog.bin`, location discoveries `.locations.txt`, quests' crew history, supported standalone legacy companion paths and per-slot data under configured WW directories. Exclude MP player snapshots/checkpoints and shared fog in SP. The participant enumeration/remap rules in `HostSaveFiles` are part of the transaction, not optional UI metadata.

Two small fixes are useful even before the full engine:

- `LocationMarkers.StateChanged` writes discoveries at native wait/SaveGame rather than scheduling all work only after return to play.
- `PoiModule.OnSaveBegin` writes the immutable spawned-site snapshot immediately instead of relying only on `OnSaveEnd`/AfterSave. Save-and-quit may reach title without the expected isPlay callback.

### S08: inventory quantity drops

Rows: all eight reserve ammo types, Grenade, Battery, **Nuts**, **Bandages x/3**, **First Aid x/3**. Nuts is native global `Money`; it is not the physical crossbow bolt item. Click a nonempty row for a quantity slider plus +/- and Drop/Cancel; keep exact selected quantities and the remainder. Empty rows do nothing. All rows fit the existing panel using 38-unit spacing.

Use native ammo boxes with `Ammo.ammo`, batteries with `Batteries.batteries`, currency can `tin_can_nuts/Nuts.nuts`, and medical `bandage_1` / `first_aid_1` with `Quantity.Quantity`. Medical counts come from the equipped quick-slot object, not an Ammo variable or the slot's potentially stale cached count. Cache by object identity, handle destroyed/replaced objects and update quick-slot display after splitting. Drop-all invokes the source's native exhausted/delete/empty behavior; never enter its heal state.

Configure inactive clones before activation; disable Randomize, name/register them for native saving, and deduct only after preparation succeeds. Grenades require one physical item per grenade because native grenade saves do not persist a stack quantity. Medical drops are one pack with N uses. Preserve native prefab IDs/quantity/save keys. SP may use its existing native item counter convention; the source GUID suffix solves independent guest-counter collisions and is not an SP requirement.

Extract the modal quantity controls and general owned-button pointer dispatch from `InventoryStyle.Multiplayer.cs`, but leave out Players, roster columns, reset/admin confirmations and all networking. The current `MultiplayerInventory.Drop` guards against offline use; replace its world/authority/ready tests with normal SP readiness, focus/input/save/modal guards and a native prefab adapter. Do not add ApocalypterMP just to access FindPrefab. The existing Guns `AmmoBox.Drop` shows a local save-registration/staging recipe but is module-private; do not introduce a CleanHUD-to-Guns type dependency.

### S09: local characters and portraits

Eight visible choices, in order: Male, Merchant, Bodyguard, Sprokka, OrganicMechanic, Female, Gungirl, Shoota. One picture-only row; female choices adjacent. Preserve existing enum IDs/config key; retained legacy Pistoleer/Mechanic IDs canonicalize to Bodyguard/Merchant. Portraits are editable, extracted missing-only and refreshed when opening settings, with matching TAB pictures. Borrowed native UI textures and UV rectangles must restore on disable/load; portrait updating runs before body creation so a failed body build does not prevent the TAB portrait.

Native skins use a private bind-pose-cancelled mesh copy plus BakeMesh for unreadable geometry. Preserve original weights/bind poses, all six full/first-person/tool/arm mesh cuts, renderer restoration and temporary-mesh disposal. Source `MultiplayerModels` already contains an offline prefab-discovery fallback; adapt local asset discovery/caches rather than copying the remote-avatar subsystem. Gungirl/Shoota asset paths come from a config-lifetime `ICharacterAssets` provider so NPC simulation can be off. Port only that provider behavior from NPC Women files, not `InitializePresentation`/female MP jobs. Preserve customized assets and existing SP male/female portrait paths through a deliberate fallback/migration.

### S10–S18: smaller shared changes and loading presentation

- **Crawl:** split native Stance-entry holstering from the scheduled movement-state check. The reported WWMP crash was Tick → SetState → Stance → Tick when disabled Movement made the convenience state getter empty. Guard against disabled/uninitialized native movement and read actual state for stance transitions. In SP the ordinary downed probe is absent, so do not claim the MP downed crash is proven there; this is defensive parity on common crawl code. Keep `CrawlMultiplayer.cs`, remote pose mode4 and downed-input exceptions out.
- **Distance haze:** the earlier density fade ran but did not remove the reported white mountains; it has been removed. In the existing AzureFogScattering render prefix, temporarily clear `fogScatteringMaterial` only for `GameRefs.MainCamera`, letting the native effect take its own unfiltered Graphics.Blit branch; restore the exact borrowed reference in the finalizer on success or exception. Apply whenever Weather is running, including clear weather and distant fronts. Weather off restores native haze; engine-baseline/native-fog-scope-off diagnostics still allow native haze. Retain WW storm walls/regional dust, sky/light scoping and map fog. This is Testing: execution is evidenced, but final visual ownership/removal of the photographed whitening remains unverified. No replacement shader, extra render job, config key or MP weather-region expansion is part of the port.
- **Weighted arms:** `ModCharacter.ArmPoseApply` blends each pose's left/right five-value arm offsets/rotations from the existing AimLift data. This is meaningful to SP NPCs/ModAPI characters too; omit `Multiplayer.RefreshRig` added elsewhere in that file. Do not change the established pose file format.
- **Compass:** include nonempty `Style.Label` with distance; adjust label height/font and refresh when the reused marker's entry changes even if distance is unchanged. Retain unchanged behavior for distance-only markers and existing visibility/ambiguity settings. No player-compass network interface is needed.
- **Recommendations:** add the default-off Debug recommendation toggle; required restart/conflict warnings remain independently visible. This is separate from the MP fork's temporarily disabled update checker.
- **Fog controls:** all four SP map-fog controls belong together in Debug > CleanHUD > Map fog of war, with no WORLD rows or generic duplicate switches. Keep Enable Debug visibility and existing values/keys/defaults. No Shared vision setting/network fog files in SP. These controls affect the map overlay, not scene haze.
- **Landed bolts:** retry native bolt mesh discovery before giving up on creating a landed visual, and translate unparented landed visuals on FloatingOrigin shifts. Keep native damage/attachment/corpse transfer behavior; no NetworkBolts/NetworkShots/NetworkImpacts or MP state fields.
- **Loading presentation:** every ten unscaled seconds choose a different cached numbered PNG with its matching tip. Pan an aspect-preserving 4% cropped UV window over the opaque black backing. Use the continuous `_driftTime` clock, first-second easing and 50 ms maximum motion step after hitches; keep the latest .12/.084 phase speeds rather than the earlier slower draft. Slide switches do not restart movement. Ordinary loading/manual saves/autosaves share this behavior. Keep progress/lifecycle and existing local enable semantics; strip the server saving/Frozen bypass. Actual main-thread loading stalls still halt rendering. See the [complete artwork/tip capture](SP-PORT-LOADING-SCREENS-2026-10-19.md), including the installed JSON update requirement.
- **Head look:** seated camera direction no longer requires gun arms. Bounded residual neck/head yaw (±85°) and pitch (±65°) account for torso aim and learned gun yaw, preserving wheel/passenger IK. Common foot animation adds residual head pitch in Body/Loco; ModAPI head pitch subtracts contributions only for humanoid spine/neck bones actually present. Port those local changes for native/female/ModAPI characters. Do not copy network pose capture/prop transforms, mode2/3 packet changes, rig-manifest refresh or handshake23 into SP.

## Current settings and user-added artwork

The [settings reference](SP-PORT-CURRENT-SETTINGS-2026-10-19.md) contains all main-root values and 22 client/client2 differences, with 3,347 parsed entries across settings.cfg/tools.cfg/main.cfg in three roots. Exact byte copies include stored default comments and current PlayerBody/WheelGrips/CleanHUD profiles. This is a dated persisted reference, not an SP default preset or proof of runtime effects. The main root currently uses 20-minute autosaves rather than the 10-minute source default. Never collapse separate client settings into one supposed current value or blindly import obsolete main-file module entries.

The [loading-screen reference](SP-PORT-LOADING-SCREENS-2026-10-19.md) inventories all 18 1920×1080 PNGs and their exact tips, with no missing/duplicate mappings. BG_MAIN.png is retained separately as a menu background; the numbered loader excludes it. All actual installed files are copied into the handover, not merely linked at the game path. They are external customized assets: the embedded fallback JSON alone does not contain the new artwork/tips. Keep filenames and associations, preserve user assets and plan the installed JSON update explicitly. Screen8/12 advertise inventory drops, so their SP content depends on S08. The supplied Screen12 `gearm ammo` spelling is preserved verbatim in the archive and documented for later editorial review.

## Additional small candidates requiring isolated review

These generic hunks were found inside MP-oriented files. They are documented so they are not lost, but are not all independently reported SP defects:

| Candidate | Source | Extract / reason |
| --- | --- | --- |
| Shared animated-character kick duration | `PlayerBody/Animation/CharPlan.cs`, kick branch | Match the local action's .55 s duration and .15 s fade. Originally fixed remote animation; assess SP ModAPI consumers/NPC use separately. The female first-person texture is already in RC37. |
| Seated gunner target changes / god mode | `Patrols/Crews/Passenger.cs` | Recheck firing immediately when `_fireTarget` changes; apply local god-mode exclusion only when the target actually is the player. Do not import MP per-player perception or remote aiming. |
| Null material/texture references in pot template | `Survival/StewPot/Factory.cs`, RegisterAsset | Return for null material and add mainTexture to ES3 localRefs only when nonnull. Separate from the already-present multi-submesh material fix. |
| Weather force ground sampling independent of rendering | `Weather/WeatherHazards.cs` plus `WeatherVisuals.SetGround` | Source probes terrain for hazard ground data even when no camera rendered a column. Introduced for dedicated hosts but can benefit offscreen SP simulation. Extract only the dependency fix; review raycast budget, avoid importing MP forces or expanded region arrays. |
| Collision player-cache refresh | `Patrols/Crews/Pilot.cs`, PlayerRef.LocalKind/Kind | Source reads cached-accessor properties rather than private fields when classifying collision targets. Treat as a small review candidate; do not import remote-player ram routing/cooldown changes. |

## Already in standard WW: avoid duplicating these ports

| ID | Improvement | Evidence / destination status |
| --- | --- | --- |
| A01 | Traveller gunshot/engine hearing and investigation | SP `ecd378b` / RC34; `Brain.cs` and the relevant Senses change. Keep this while selecting S03 combat observers; most remaining Senses differences are MP perception. |
| A02 | PLAYER / WORLD / HUD settings split; storm tuning under Debug | SP `aed04ea` / RC35. SYSTEM/save library and S15 are the remaining distinct follow-ups. |
| A03 | Motorcycle independent arms, straight/left/right tuning, seat/torso offsets, shared 96-value handlebars profile, menu-only editor start, missing-wheel fallback | SP `aed04ea` / RC35; `WheelGripEditor.cs` and embedded handlebars defaults compare equal. Remaining `WheelArms` passenger gate is MP-only. |
| A04 | Core forced-off color grading and locked graphics row | SP `aed04ea` / RC35; `Core/ColorGrading.cs` compares equal. Do not add a second policy or change other post effects. |
| A05 | Main Low Crawl feature, embedded humanoid loop and reproducible authoring files | SP `2d56c6d` / RC36. `CrawlBody.cs`/embedded bundle are already present. Only S10's reentrancy follow-up remains. No new SP bleedout system was added. |
| A06 | Coyote Merchant / Mechanic / Organic Mechanic idle exclusion | SP `2d56c6d` / RC36; `NPC/Brain/Idle.cs` compares equal. No teleporting old displaced merchants. |
| A07 | Quest conversation mouse-look freeze and temporary authored-voice suppression | SP `2b6a869`, released in `2d56c6d` / RC36. Voice implementation compares equal; remaining PluginRuntime differences concern MP party behavior. |
| A08 | Female first-person kick atlas | SP `7208ca0` / RC37. Embedded texture and arm-side implementation already present; preserve custom texture override behavior. |
| A09 | Improved male/female player GLBs | Destination RC35 release notes record their inclusion. External installed/release GLBs are outside this source-file audit; do not replace them blindly. |
| A10 | Stew Pot all-submesh material slots | Present in the **SP working tree** `Survival/StewPot/Factory.cs`, uncommitted and not part of RC37 HEAD. Preserve/include that pending fix rather than reapplying it. MP/SP Factory diff is currently the separate null guards above. |
| A11 | Visible seated melee, FP-timed upper-body swings, held lances and retained grenade/lance throw torso motion | Follow-up common fix now present in **both working trees**, `PlayerBody/Body/Body.cs`; 17 additions/nine removals per repo. Existing MP modes2/3 carry the corrected final pose/held prop with no protocol change. MP deployed, SP built/staged only; see [scope, clip limitation and checks](seated-melee-throws.md). It reuses retargetable third-person clips timed to native FP attacks, rather than copying raw nonhumanoid FP arm curves. |

Existing SP docs provide more detail: `V:/Repos/Apocalypter/WildWasteland/notes/release-0.1.37-RC34.md`, `release-0.1.38-RC35.md`, `release-0.1.39-RC36.md`, `release-0.1.40-RC37.md`, `mp-bike-defaults.md`, `female-kick-texture.md`.

## Leave MP-only work out

- Active/sleeping player tab, host reset/self-reset administration, sleeper bodies/carry/handoff/body removal, reconnect recovery, unlimited downed revival and explicit multiplayer respawn anchors. The inventory rows/quantity dialog are S08; the rest of `InventoryStyle.Multiplayer.cs` is excluded. A standalone rescue-to-start feature would be a separate product decision, not an automatic port of host Reset.
- Native bridge/type adapters, lobby/host/join/name/IP/Steam menus, three-instance launchers, local identity/save-root redirection, strict mod manifest handshake, chat/notices, ordered reliable UDP envelopes and all WW packet types.
- Guest-to-guest dropped guns, ownership/claim/adoption, final car snapshot barriers, guest motorcycle owner balance, passenger exit streaming safeguards, remote flashlight and avatars/equipment/motion/punch/kick replication.
- The latest seated-aim network half: fitted seated prop identity/relative pose, receiver pose-item selection, first-person network-capture compensation and NPC manifest refresh. S18 selects only common local head/look behavior. The latest upright guest wake/respawn half is also excluded; S19 selects native offline save loading.
- Shared map fog/vision and player-colored compass/map arrows. S13's generic label display is independent of those player features.
- Host-authoritative quests/rewards/party inventory, creature perception for several players, vehicle targeting/awareness transfer, network NPC/traffic/stew/web/projectile/impact/weather presentation, 64-region multi-player storm tracking and dedicated-headless hosting.
- MP unpaused-menu/WorldRunning changes, Apocasetter time-scale overrides and Performance-off policy. Standard SP keeps its existing pause behavior and performance features. Copying these broad module diffs would silently change SP behavior.
- MP repo/plugin branding, URLs, update-check suppression and version. Standard WW's updater and release channels remain its own. The temporary disabled MP checker is not a general update-policy request.

## Preserve destination-only improvements

SP RC37 changed the ignition default/fallback/description to **I**, while the current MP source still defaults to **E**. Keep SP's I and preserve existing valid user bindings. Do not copy MP PlayerBodyModule/IgnitionControl wholesale while porting character choices.

Keep destination version/identity (currently 0.1.40), standalone paths, native/module prefab IDs and save keys, custom profiles/assets, established MP-free optional-mod integrations, and all existing pending changes. Most source module changes are additive MP guards, not missing SP behavior. The file inventory is a comparison aid, not a list of files to overwrite.

## Suggested implementation order and acceptance

1. Small isolated fixes: S07's POI/discovery save timing, S10, S11, S12, S13, S14, S15, S16, S17, S18; review the additional candidates individually. They can be assessed without a complete new save system. S17's source can land independently, but ship the supplied drop-related tips with S08.
2. S08 local inventory drops. Keep the existing TAB modal/pause/texture lifecycle; no dependency on ApocalypterMP or a second input-block implementation.
3. S09 local characters/portraits and config-lifetime female asset provider, preserving SP ignition and existing custom portrait/model paths.
4. S01/S02/S05/S07 save transaction, library, stable metadata/history and full participant integration as one coordinated change. Do not ship the new library with the old unsafe/partial save path underneath it.
5. S03/S04 autosave/countdown/combat/vehicle gates plus S19 seated-save loading/terrain handoff; S06 cleanup last, default off. Use the captured settings/assets selectively and retain destination customization.

For each implementation, follow the destination AGENTS.md, build on Windows against standard WW, retain its standing no-base-game-deployment policy unless Denis changes it, and give Denis in-game checks. No game launch, automation or test harness is authorized by this documentation request. Source compilation/history does not establish gameplay or performance acceptance.

Minimum manual acceptance for the eventual port:

- Run with ApocalypterMP entirely absent. Native slots, named Continue/load, fresh-world separation, create/overwrite/delete/restore, manual/exit/vehicle/autosave and interrupted-publication recovery must retain complete saves and companions. No forced vehicle exit for saving; loading seated saves places the player standing beside a stopped, braked vehicle. Test old/new saves, blocked/late main terrain and clearance, restored collision/stance, driving after load and no spinning/crawl fallback. Check native/CleanHUD-off saving UI and Apocasaver coexistence policy.
- Trigger combat during countdown/final warning: ten quiet seconds then full countdown, no false failed-save/reload-required state. Check NPC-off and vehicle fights; search ghosts/empty cars must not defer forever.
- Explore/discover a POI, save-and-quit directly, reload: spawned-site history, map discoveries/fog, NPC senses, quest/crew history and Patrols state agree with that snapshot. Backup/restore rewinds all relevant files together.
- Drop partial ammo/battery/Nuts/medical quantities and all grenades, cancel, exhaust/replace medical packs, pick up with capacity limits, save/reload. No duplication/refills/lost remainder, and no healing when splitting.
- Eight local characters in first/third person, tool/weapon arms, TAB portraits before body build, unreadable native meshes, NPC module off, custom textures/portraits and asset reload. Check crawl holstering/clearance/no strafe and no recursive state entry.
- Inspect native haze with Weather on/off, clear/approaching/inside/cleared storms, on foot/in cars and camera pitch changes; retain WW wall/dust and map fog. Verify the four map-fog controls appear only in Debug with prior values. Check compass labels/pooling, edited ModAPI arm poses, seated/foot/native/female head look with guns/holster and landed bolts across a floating-origin jump.
- Load all 18 captured screens with exact tips, check aspect ratios/black coverage and ten/twenty-second transitions with continuous drift. Retain current custom profile/settings values through upgrade; validate independently of MP, legacy main-file values and any forced runtime policy. Retain Testing until Denis validates visuals/performance.

## Source handovers

- [Save behavior, transaction lessons and limitations](../compat/ApocalypterMP/HOST-SAVES.md)
- [Inventory, medical splits and the MP-only roster/reset boundary](../compat/ApocalypterMP/INVENTORY-PLAYERS.md)
- [Character body/portrait source history](../compat/ApocalypterMP/CHARACTER-BODIES.md)
- [Low Crawl source, asset provenance and checks](low-crawl.md)
- [Merchant idle scope](camp-merchant-idle.md)
- [Quest input/voice scope](quest-ui-input-voices.md)
- [Motorcycle calibration/backport history](../compat/ApocalypterMP/ISSUE-8-HANDLEBARS.md)
- [Latest distance-haze and Debug placement correction](weather-distance-haze.md)
- [Seated aim/head look and MP-only attachment boundaries](../compat/ApocalypterMP/POSES-EQUIPMENT.md)
- [Current installed settings and complete value tables](SP-PORT-CURRENT-SETTINGS-2026-10-19.md)
- [All installed loading screens and exact tips](SP-PORT-LOADING-SCREENS-2026-10-19.md)
- [Seated melee/throw follow-up already applied to both repos](seated-melee-throws.md)

Audit outcome: this task updates documentation/source comparison inventory and captures installed settings/artwork as reference copies. It does not port production code, rebuild/deploy either DLL, change live saves/configs, commit, push, release or mark features Verified.
