# Loading artwork and tips for the singleplayer port

Captured 2026-10-19T11:57:32.840479+00:00 from `E:/SteamLibrary/steamapps/common/Apocalypter/BepInEx/plugins/WildWastelandMP/LoadingScreens`. The actual PNGs and `screens.json` are preserved in [the reference folder](sp-port-reference-2026-10-19/LoadingScreens/), with dimensions, byte sizes and SHA-256 in [loading-screens.tsv](sp-port-reference-2026-10-19/loading-screens.tsv). The installed files were read only; no artwork or text was edited.

There are **18 numbered loading images**, each with exactly one matching JSON tip, plus `BG_MAIN.png` (menu background, excluded from loading selection). Screen6–Screen18 have October 19 modification timestamps; Screen1–Screen5 and BG_MAIN have earlier timestamps. Timestamps establish the captured files, not authorship or when each was first introduced. Include all 18 together, including the recently replaced earlier-numbered artwork.

## Port and packaging contract

The loader scans the installed `LoadingScreens` folder for positive-numbered `ScreenN.png` files, sorts numerically, and uses case-insensitive filename matches for tips. `screens.json` uses version1 with a `screens` array of `image`/`text`. Keep image names and tip associations exact. A PNG without a matching JSON entry can display with blank text; an entry without a PNG cannot render an image. This capture has neither mismatch nor duplicate tip names.

**These customized PNGs are installed assets, not embedded in source.** The repo only embeds its fallback `src/Modules/CleanHUD/Embedded/LoadingScreens/screens.json`; packaging that fallback alone loses the new artwork and tips. Ship the captured Screen1–Screen18 PNGs and matching screens.json into the standalone plugin's `LoadingScreens` directory through its normal release-assets process. Preserve existing user customizations; extraction only writes a missing JSON file, so an older installed JSON is not automatically upgraded by changing the embedded resource. Decide default-asset updates explicitly during the port, without a blanket overwrite of user assets. BG_MAIN belongs to menu presentation and is retained separately in this capture.

S17 covers the source behavior: opaque full-screen black backing, aspect-preserving crop/4% overscan, continuous eased unscaled UV drift capped at 50ms per step and a different random image with matching tip every ten unscaled seconds. Keep ordinary load/manual-save/autosave paths; omit the MultiplayerSaving.Frozen/server-save adapter. Standard WW already has the basic image/tip loader. Check 16:9, 4:3 and ultrawide crops, readable tip lines, no uncovered edges, transitions at ten/twenty seconds, and normal completion. Main-thread loading stalls still stop rendering; smoothing cannot render through those stalls.

The supplied tips are copied verbatim. Screen8 and Screen12 depend on S08 quantity/equipment drops; ensure that port is present before presenting those claims. Screen12 currently says `equipped gearm ammo`; retain that source text in the archive and treat any spelling edit as a separate content change. No MP-only player-management tips are present. Tip wording is supplied content, not fresh gameplay verification.

## Captured screens and exact tips

| Image | Dimensions | Bytes | Exact tip |
| --- | --- | ---: | --- |
| [Screen1.png](sp-port-reference-2026-10-19/LoadingScreens/Screen1.png) | 1920 × 1080 | 156179 | To survive in the wasteland, always pay attention to your surroundings. The compass can help if you turn the compass indicators on. |
| [Screen2.png](sp-port-reference-2026-10-19/LoadingScreens/Screen2.png) | 1920 × 1080 | 507110 | Beware of warboys. They use blast lances and can jump to their death. |
| [Screen3.png](sp-port-reference-2026-10-19/LoadingScreens/Screen3.png) | 1920 × 1080 | 398532 | Enemy vehicle patrols roam the wastes. Stay away, fight or flee. |
| [Screen4.png](sp-port-reference-2026-10-19/LoadingScreens/Screen4.png) | 1920 × 1080 | 577417 | To stop a travelling Coyotes merchant, honk or yell. |
| [Screen5.png](sp-port-reference-2026-10-19/LoadingScreens/Screen5.png) | 1920 × 1080 | 163403 | Beware! In Wild Wasteland, animals and mutants have learnt some new tricks. |
| [Screen6.png](sp-port-reference-2026-10-19/LoadingScreens/Screen6.png) | 1920 × 1080 | 151191 | If you add water and raw food to a stew pot and boil it periodically, it never spoils. Eternal stew! |
| [Screen7.png](sp-port-reference-2026-10-19/LoadingScreens/Screen7.png) | 1920 × 1080 | 219210 | Press 'crawl' to get to hard to reach places. |
| [Screen8.png](sp-port-reference-2026-10-19/LoadingScreens/Screen8.png) | 1920 × 1080 | 129760 | Hold Reload to unload the gun in your hands. You can also drop equipment, ammo and nuts from your inventory screen. |
| [Screen9.png](sp-port-reference-2026-10-19/LoadingScreens/Screen9.png) | 1920 × 1080 | 92794 | Binoculars help you see the location name and allegiance. They also help to uncover more of the 'fog of war' on the map. |
| [Screen10.png](sp-port-reference-2026-10-19/LoadingScreens/Screen10.png) | 1920 × 1080 | 231223 | Excess armor drops as plates when changing to lower-capacity armor; each complete 50 excess points produces one plate. |
| [Screen11.png](sp-port-reference-2026-10-19/LoadingScreens/Screen11.png) | 1920 × 1080 | 159049 | Pick up matching bandages or first aid to refill a partially used pack to three uses. Any excess stays on the ground. |
| [Screen12.png](sp-port-reference-2026-10-19/LoadingScreens/Screen12.png) | 1920 × 1080 | 119577 | Open the inventory screen and click equipped gearm ammo, nuts or first aid items to drop them. |
| [Screen13.png](sp-port-reference-2026-10-19/LoadingScreens/Screen13.png) | 1920 × 1080 | 194400 | Gunshots, explosions, engines, horns, shouts and thrown items can draw enemies. |
| [Screen14.png](sp-port-reference-2026-10-19/LoadingScreens/Screen14.png) | 1920 × 1080 | 187409 | Enemies investigate your last known position after losing sight. Moving away can shake a search. Hounds and other tracking creatures can follow you more effectively. |
| [Screen15.png](sp-port-reference-2026-10-19/LoadingScreens/Screen15.png) | 1920 × 1080 | 111562 | Darkness reduces enemy sight range, but your flashlight can reveal you. |
| [Screen16.png](sp-port-reference-2026-10-19/LoadingScreens/Screen16.png) | 1920 × 1080 | 287523 | Scrapyard patrols can include working two-wheel motorcycles. |
| [Screen17.png](sp-port-reference-2026-10-19/LoadingScreens/Screen17.png) | 1920 × 1080 | 408794 | The War Rig tanker has an accessible interior and hitches to the Rustchief. Inside the War Rig tank, you are protected from storm damage and tornado forces. |
| [Screen18.png](sp-port-reference-2026-10-19/LoadingScreens/Screen18.png) | 1920 × 1080 | 191759 | Aim at a friendly human nearby and press F to ask about work or hiring. Press J to review your jobs and deadlines. |
| [BG_MAIN.png](sp-port-reference-2026-10-19/LoadingScreens/BG_MAIN.png) | 1672 × 941 | 2395847 | Menu background only; no loading tip |
