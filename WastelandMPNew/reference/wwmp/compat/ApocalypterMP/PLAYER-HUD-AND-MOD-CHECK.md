# Player HUD, MP updates and strict mod matching — 2026-10-18, Testing/source only

Denis requested camera-facing floating names; MP-only HUD options for Floating names, Players on compass and Players on map; colored direction arrows for every map player including self; updates from the MP repository; and rejection with an explanation for version/mod differences.

## Player presentation and options

Native `Game.RemotePlayer.Update` rotates its child TextMesh from `Camera.main.transform.position`. WW's `PlayerBody/Camera/ThirdPerson.PreCullInner` changes the render matrix without moving that camera transform; native update-time billboarding therefore targets a different view. `Core/MultiplayerNames.cs` now aligns name tags with the actual `cameraToWorldMatrix` in `Camera.onPreRender`, after all PreCull overrides. World rotation cancels inherited player yaw and follows camera up. Native text, downed coloring and world depth/size stay native. Render alignment is a source correction; Denis must confirm the reported appearance in game.

The bridge caches native tags/peer IDs/colors from a postfix on `RemotePlayer.Update`, removes entries in a prefix on `RemotePlayer.Destroy`, and clears disconnected sessions. No scene search, new update component or separate polling job. Hooks belong to Core's existing MultiplayerBridge owner; PlayerBody can be disabled without removing this presentation.

F3 > HUD shows the following only when the MP bridge is present:

| Label | Setting | Default |
|---|---|---|
| Floating names | settings.cfg `[Multiplayer] FloatingNames` (new) | On |
| Players on compass | existing `[CleanHUD / HUD] ShowPlayers` | On |
| Players on map | `[CleanHUD / HUD] ShowPlayersOnMap` (new) | On |

All three are local HUD preferences, editable by guests and excluded from host world policy. Players on compass moves out of WORLD/initial-host setup. Map arrows use native `UI.CompassBar.ColorOf(peer)` for both the local player and remote players. Native remote roots supply live facing/position in each process; the local arrow is drawn last, once, with the local MP color. The existing map projection/zoom, 18x21 arrow and 22x25 outline remain. Turning map players off hides all player arrows, including self. Floating names Off does not disable map players. Offline retains the original local green arrow; CleanHUD shutdown restores its borrowed native player icon.

## Updates

CleanHUD Menu's release API, release tag links, User-Agent and GitHub button now use `DeonUrist/WildWastelandMP`, matching `git remote origin`. It no longer offers base WW releases. Existing version parsing and release selection remain. Denis subsequently requested suppression for now: internal `Menu.UpdateChecksEnabled` is false, so the coroutine exits before making any HTTP request and never creates an available-update result. The checker and MP URLs remain ready to reuse; no new configuration entry was added.

Live metadata check with authenticated `gh api repos/DeonUrist/WildWastelandMP` reports `private: true`. Anonymous web access returns 404. The in-game checker is unauthenticated, so it cannot discover private releases yet. URLs are corrected; working public update notifications require public repository access or a separately chosen distribution/authentication design. No visibility change, embedded credential or publication was performed.

## Exact loaded mod compatibility

Previous handshakes compared only a WW patch revision. Revision **15** now includes a bounded manifest in client Hello and host acceptance. Each loaded BepInEx plugin contributes GUID, display name, declared version and the loaded assembly's module version ID (MVID). It is captured at connection time after chainloading, with deterministic GUID ordering and a maximum of 512 plugins; extended packets remain capped at 60,000 bytes. Destroyed Unity plugin objects are checked by managed reference, preserving their actual loaded assembly identity.

Host rejects before native welcome/world admission if a client lacks a host plugin, has any extra plugin, has a different declared version, or has a different loaded DLL build at the same version. No cosmetic/client-only exemptions were added. The client checks the host manifest before applying host settings or accepting world launch. Same native protocol but missing/old WW patches get an explicit required-build/revision explanation; native protocol mismatch retains its native rejection. Manifest validation includes duplicate/count/string-length/trailer checks. The manifest covers loaded BepInEx plugins, not disabled files, configuration values or asset artwork; MVID is a loaded-build identifier, not cryptographic file attestation.

Reasons identify the offending name/GUID and versions when relevant, with instructions to install the host's version/DLL/mod set. Native rejection delivery/end reason remains in use; client-side validation now calls native `End(reason)` so its reason survives Stop. The MULTIPLAYER panel shows the reason in a wrapped, dismissible CONNECTION CLOSED panel instead of relying only on its narrow status line. Native title notices remain the fallback outside that panel. Matching joins log `[Multiplayer] Exact mod versions/builds accepted; host world settings applied before world launch`.

## Verification and retest

Source review and whitespace checks only. Nine source files (two new Core files), roughly 200 added/removed source lines. New config entries are the two rows identified above; existing ShowPlayers key/value is retained. No native save fields or external/base DLL changes. No build/deploy/new MD5 under the standing explicit "dll build later" deferral. No game launch, harness, commit, push, release or repository visibility change. Compilation, runtime visuals/handshake installation and performance remain pending.

After a later authorized Windows build/deploy to both peers:

1. Open HUD on host and guest. Confirm the three MP-only rows are present and each guest switch changes only that guest's HUD; WORLD no longer lists Players on compass. Offline without the MP bridge must omit these rows.
2. Have the other player turn, walk, crouch and sit; orbit in third person and move/look in first person, including high/low camera angles. Names should stay parallel to the actual viewing plane rather than inheriting player rotation. Test Floating names Off/On and loading/save screens, retaining native downed text/color.
3. Open/zoom the map on both peers. Each player, including self, should have exactly one arrow in the same MP color in either process. Turn and move each player; arrows should follow heading/position. Turn names/compass off independently; map arrows should remain. Players on map Off must hide all player arrows, On restore them. Rejoin/leave removes stale arrows. Retest seated players, fogged regions and offline local arrow behavior.
4. Start both processes with identical loaded plugin DLLs. Join should succeed and print the acceptance log. Using an isolated test install, try one missing plugin, one extra client plugin, a different plugin version and a different DLL build with the same version. Each join must be refused before world admission with the appropriate mod/version/build explanation; restore identical mods and confirm rejoin. Do not change live plugins while games are running.
5. Verify an old/missing WW patch and native MP protocol mismatch also refuse with clear reasons. Repeated join attempts and closing/reopening MULTIPLAYER should preserve readable reasons without replaying a dismissed panel every frame.
6. For this build, confirm there is no automatic update request, `WW MP update check:` log or UPDATE AVAILABLE button. The GitHub button still targets the MP repository. Only after Denis requests re-enabling and the MP release feed is accessible should release discovery be tested again.
