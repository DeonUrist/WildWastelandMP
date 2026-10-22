# Wild Wasteland 0.1.7 RC4

Release candidate 4 adds optional compatibility with **Apocatalker 2.0.0** and **Apocalypter Quests 1.8.0**. Numeric BepInEx version **0.1.7**; tag **v0.1.7**. All RC3 features, defaults and external assets are retained. This is a prerelease; gameplay and performance verification remain **Testing**.

## Changes

- CleanHUD dialogue with wrapped chat, scrolling choices and working original/quest callbacks. Disabling CleanHUD restores the original Talk window.
- Native map and compass quest flags, plus green turn-in diamonds that follow their giver. Existing Immersive Map integration remains supported when installed.
- Standalone Talk/Quests callbacks run through WW's named scheduler jobs. Repeated full-world item/people discovery becomes a 500 microsecond sliced cache; quest logic runs every 0.5 seconds.
- Hired followers keep Quests' following and seating behaviour, with WW weapon animations, shot/reload clips, native firing holds and explicit passenger combat targets.
- Preserve the existing `ApocalypterQuests` ES3 key, serialized data and Talk's global/manual StoryProfile files. Quest saves run after the native save flush through SaveBus.
- Add `[QuestsCompat] Enabled` (default true) in settings.cfg. Original Talk/Quests settings remain authoritative; no config/save migration is needed.

## Installation and verification

Extract `WildWasteland-0.1.7-RC4.zip` into `BepInEx\plugins`, creating/updating `WildWasteland\`. Keep existing configuration/data. Remove/disable absorbed standalone mods listed in README, including standalone CleanHUD.

**Keep Apocatalker 2.0.0 and Apocalypter Quests 1.8.0 installed separately**, including their original adjacent data/theme/voice folders. These DLLs are optional dependencies and are not bundled. Without them, compatibility remains idle. Optional Apocapocket 2.0.10 support from RC3 is retained.

Confirm `Loading [Wild Wasteland 0.1.7]`, `[QuestsCompat] Follower bridge ready` and `[CleanHUD] Apocatalker dialogue bridge ready`. Test conversation/quest buttons and fallback, map/compass markers and turn-in, hired followers fighting/reloading on foot and from a car, save/load and autosave. [Detailed instructions and limitations](https://github.com/DeonUrist/WildWasteland/blob/v0.1.7/notes/compat-Talk-Quests.md).

If native plugin components have been destroyed on a scene change, restart to compare fully standalone behaviour after disabling the bridge. Source callback execution is measured by the scheduler but cannot be hard-preempted; no performance improvement is claimed without in-game profiling.

Validation: Windows Release/Deploy build passed with zero errors and seven existing warnings. Built/deployed DLL MD5: `D748A85A07DE726EC219B800472CD494`. Gameplay and performance acceptance remain Denis's task. This candidate does not mark features Verified or change the Apocasetter index.

[Previous candidate: 0.1.6 RC3](https://github.com/DeonUrist/WildWasteland/releases/tag/v0.1.6) · [Feature documentation](https://github.com/DeonUrist/WildWasteland/wiki)
