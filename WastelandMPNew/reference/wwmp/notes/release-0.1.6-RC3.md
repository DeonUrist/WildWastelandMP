# Wild Wasteland 0.1.6 RC3

Release candidate 3 adds optional Apocapocket 2.0.10 compatibility to CleanHUD. Numeric BepInEx version **0.1.6**; tag **v0.1.6**. All RC2 features, reviewed defaults and external assets are included. This is a prerelease; in-game verification remains **Testing**.

## Changes

- Show three to six logical inventory slots according to the equipped backpack, fitting them inside the existing CleanHUD slot row.
- Keep native-slot icons correct when an extra-slot weapon borrows a native holder, and highlight the logical extra slot.
- Show ordinary item icons and an ITEM presentation with weapon ammo fields hidden; retain the held item's identity after retrieval.
- Preserve the last stable inventory display during slot transactions, loading and save normalization.
- Hide late-created native pocket widgets and restore them when CleanHUD is disabled. Keep CleanHUD-owned panels out of Apocapocket's native card shifting.
- Display current primary slot bindings and Item 1-6 labels. Existing alternative-key rebinding and conflict handling are preserved.

Apocapocket remains a separate, optional plugin; install/enable 2.0.10 separately to use this integration. Without it, CleanHUD retains three slots. There are no new config entries, save keys, save formats or bundled Apocapocket files.

## Install and test

Unzip `WildWasteland-0.1.6-RC3.zip` into `BepInEx\plugins`, creating `WildWasteland\`. Keep existing configuration/data files. Disable/remove absorbed standalone mods listed in README, including standalone CleanHUD; retain optional Apocapocket. The archive includes the DLL, all RC2 assets, loading screens/tips, README, defaults audit and compatibility test steps.

With Apocapocket 2.0.10 enabled, confirm `Loading [Wild Wasteland 0.1.6]` and `[CleanHUD] Apocapocket inventory bridge ready (2.0.10)` in LogOutput.log. Check 3/4/5/6 slots with no/small/medium/large backpack; fill native slots and draw/holster an extra-slot weapon; retrieve an ordinary item; rebind keys; save/load; toggle CleanHUD off/on. Expect correct selection/icons/ammo, ordinary-item presentation, native UI restoration and no duplicates. [Detailed steps](https://github.com/DeonUrist/WildWasteland/blob/v0.1.6/notes/compat-Apocapocket-CleanHUD.md).

Windows Release/Deploy compilation succeeded with zero errors and seven existing warnings. Built/deployed DLL MD5: `71B9626142848EA7C0AD4A82A6CC6D66`. Game and performance acceptance remain Denis's task; this release does not mark features Verified or update the Apocasetter index.

[Release candidate 2](https://github.com/DeonUrist/WildWasteland/releases/tag/v0.1.5) · [Feature documentation](https://github.com/DeonUrist/WildWasteland/wiki)
