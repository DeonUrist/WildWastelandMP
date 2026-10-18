# Step 10 — per-item magazines and offline character choices

Per-gun rounds/hammer now append deterministic runtime fields to native ItemVars. The current owner captures the loaded arm model into that physical weapon; native ObjectVars and SavedItem.Vars carry it through transfer/inventory restore. Draw imports that item's state before choosing its magazine. Host saves import replicated magazines into the existing Apocaunloader.Magazines ledger, retaining the original key/format for offline loads. A pre-flush callback captures the selected magazine even while gameplay scheduler jobs are paused for saving. No saved component, item identity or prefab ID changes.

The six native MP character choices are now available without the MP DLL too. Offline source selection caches native asset Animators; normal WW player/Mixamo animation ownership is retained. Existing character config values remain unchanged.

Windows Release Deploy=false: zero warnings/errors. No deployment/commit/runtime verification.

Required manual coverage: fire/reload/unload/holster/transfer each firearm, death/revival/reconnect inventory and dropped items, host save/offline reload, fresh versus previously claimed per-type ammo, module toggling and modded weapons. Native profile/health/needs inventory persistence remains MP-owned; receipt capacity/order and per-world character preferences still need final review. Crossbow attachment corrections and source-independent save validation remain open.

DLL MD5: C4014A8F1B2AC90834D3511CD5C14382
