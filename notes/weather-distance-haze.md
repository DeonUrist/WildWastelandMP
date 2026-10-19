# Distance haze removal — 2026-10-19 — Testing

Denis still sees white distant mountains in front of the sand wall and explicitly wants no distance whitening. WORLD's Fog of war/edge/opacity settings control the map overlay, not scene atmosphere.

The installed DLL MD5 was `F7279DBE92071B9BF2EEA9CCD70CF3E1`. Current `BepInEx/LogOutput.log` confirms the earlier `pale distance/height haze fully fades during approach` handler executed during a natural storm. Missing execution/stale installed bytes are not supported explanations. The prior density-only attempt is removed, not stacked with another fade. Its removal did not prove which shader caused the photographed effect; final visual ownership remains Denis's check.

`Sources/code/Assembly-CSharp/UnityEngine.AzureSky/AzureFogScattering.cs` shows the native image effect always invokes the fog-scattering shader when its material exists, and already offers an unfiltered `Graphics.Blit(source,destination)` branch when the material is null. `Assets/raw/Shader/unnamed__23.shader` identifies that shader as `Azure[Sky]/FogScattering`; `Assets/raw/Material/Fog__255.json` identifies its shared material. `Sources/data/hierarchy/scene1_game/PlayerCameraHolder.txt` and vehicle prefab trees show the effect on both on-foot and driving cameras.

WeatherSky's existing prefix now temporarily clears that material only for `GameRefs.MainCamera`. The existing finalizer restores the exact borrowed reference on success or exception. The original native method still runs, using its own unfiltered branch; no new render target, replacement image effect, extra blit, readback, shader bundle, saved component, scheduler job or patch target. Native distance scattering is removed whenever Weather runs, including clear weather and distant fronts, rather than gated on storm amount/proximity. WW regional dust/wall, sky/light scoping and the map fog remain. Disabling Weather unpatches the handler, restoring native haze. Engine baseline / Native fog scope OFF diagnostics retain the native pass for isolation. Host/offline and guest presentation install the same handler.

One source file: 14 additions / 26 removals. No Core/base-game/base-WW/external-DLL/config/save/network changes, game execution, harness, commit or push.

Denis checks after restarting all instances with matching DLLs:

1. Keep Weather enabled and temporary diagnostics off. Load the same Fog Test world, look at the same mountains in front of the wall, and pitch down/level/up. Their surfaces should retain normal color, with no Azure distance whitening.
2. Compare before/during storm approach, inside dust and after the storm leaves. Native haze should stay absent; WW sand-wall/dust should remain visible and still obscure objects inside the storm.
3. Repeat on foot, in a vehicle, first/third person and host/guest; check pause/resume and save/reload. Toggle Weather off/on to confirm native haze returns only while off. Map Fog of war should retain its existing behavior.

Confirmation during an active storm: `[Weather] Native Azure distance haze bypassed: gameplay camera uses the unfiltered image; WW regional dust retained.` This confirms handler execution, not visual acceptance or performance.

Windows Release/Deploy=false succeeded with zero warnings/errors. Built/staged MD5 `46263D23F92559388734E146F296334B`; exact unique stage `bin/Release/WildWastelandMP_no-distance-haze_20261019_140142.dll`. Three games (27052/36584/47980) remained running; disabled WW and WWMP manifests absent. Not deployed. Installed DLL changed concurrently from the initial F7279 hash to `EC32E6D143DD14606B9959FCAC5518EB`; that is not this fix. Deploy this exact stage after all games close, checking for intervening work first. Build contains current shared-tree changes; no live settings/saves touched.

## Deployment and Debug placement follow-up

Denis closed all games and asked to move WORLD fog settings into Debug before deployment. CorePages now groups Fog of war, Shared vision, reveal radius, edge fade and opacity under F3 > Debug > CleanHUD > Map fog of war. WORLD and HOST WORLD no longer draw those rows. Generic Developer/switch lists omit duplicates, and the guest HUD hint points to the host's Debug controls. Existing config keys/values/defaults, Debug visibility gates and multiplayer authority are retained. No live config edits.

Combined Windows Release/Deploy=false passed zero warnings/errors. Exact stage `bin/Release/WildWastelandMP_haze-debug-fog_20261019_140744.dll` deployed with all games closed and both disabled manifests absent. Built/staged/installed MD5 `7097EC32CB3267C1EBCA5D1659843E8F`. This supersedes the undeployed 46263 stage above; includes shared-tree changes and both requested changes. No game execution/harness/commit/push.

In addition to the visual checks above, verify WORLD/HOST WORLD no longer show map fog controls; enable Debug through SYSTEM if needed and open Debug > CleanHUD > Map fog of war. All five settings should retain their previous values. Changing map fog should retain its prior map behavior and host ownership; it does not control scene haze.
