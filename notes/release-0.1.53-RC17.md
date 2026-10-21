# Wild Wasteland MP RC17 (0.1.53) — Merchant stock and first-spawn loot

Merchant buses now reconcile tracked sale items every three seconds using cached FSM references. Paid purchases remain available to other players; unpaid stock is free while Coyotes are hostile and returns to its recorded price when friendliness returns. A friendly guest's local zero sale flag is allowed to await host purchase confirmation rather than being independently charged again. New stock is registered after its price and sale flag are initialized.

Fresh trader buses use empty ordinary loot. A bounded check inside their PhysicsLock cargo box removes newly appeared non-trader native items only during the bus's first spawn. It preserves registered pre-existing objects, fitted parts and exact trader stock. It does not run on unload/revisit or as a continuing sweep. Revisited buses restore their recorded cargo; future cargo snapshots exclude trader stock already restored separately by the merchant record, preventing duplicate loose goods.

Version **0.1.53**, tag **v0.1.53**, handshake **27** retained. **Update every peer.** Existing merchant JSON gains optional hostility/object-identity fields; no new config, packet kind or top-level save key. First-spawn detection uses existing traffic snapshot fields and an unsaved runtime flag. RC16's save timeout and earlier fixes remain included.

Runtime, multiplayer and save-integrity verification remain **Testing**. Old records already marked purchased or containing legacy duplicate cargo are retained without speculative deletion. The spawn audit skips missing/full PhysicsLock queries with a warning and cannot remove mod spawns occurring later, outside its box or without native saved-item/ForBuy identity.

Install **BepInEx 5 x64** and the separately distributed [Apocalypter Multiplayer mod 0.4.1](https://www.nexusmods.com/apocalypter/mods/10), then extract the complete release ZIP into `BepInEx/plugins`. Keep standard Wild Wasteland disabled.

Manual verification:

1. With a host and two guests on matching builds, buy as each player in turn; another player must take/use the same bought item without paying again. Include guest-to-host-to-guest, reconnect and late join.
2. Trigger Coyote hostility, leave some unpaid stock and take another item. Wait for friendliness: remaining unpaid stock must cost its recorded price; paid/taken items remain usable. Another faction attacking Coyotes must not itself change the player's relation.
3. Spawn a fresh bus, including with the suspected loot-spawning mod. Check its sale slots, fitted engine/parts and absence of unrelated cargo. A first-spawn cleanup logs `Removed non-trader item from merchant spawn PhysicsLock area: ...`.
4. Leave personal cargo aboard, unload/revisit repeatedly, then save/reload. Saved cargo and remaining stock must restore without new loose-stock duplicates or another deletion audit. Test buying just before delayed host confirmation too.

No game/harness launch, live save/config/base edits, or changes to other repositories. Implementation details and historical test stages: [merchant-stock-reconciliation-2026-10-21.md](merchant-stock-reconciliation-2026-10-21.md).

Windows Release/Deploy=false passed with zero warnings/errors. Exact stage `V:/Repos/Apocalypter/_releases/WildWastelandMP_0.1.53-RC17a.dll`; built/staged MD5 **4CE607DA1ED271838D52EFE902CAF13D**. Runtime-only ZIP: **287 files**, **236,692,449 bytes**; SHA256 **6ea6a764538bdc87f3899d0c7e6163ed3b8e533ea7af9a700dcc5d8301bba307**. CRC, exact bytes, safe unique paths, single DLL and 18 loading screens checked. All **285** RC16 assets other than the DLL and intentionally emptied Coyote_Trader template are byte-identical; **155** base-release assets retained unchanged. Standard-WW installation lacks the active MP folder/dependency, so staged only. Official non-prerelease release candidate for latest discovery.
