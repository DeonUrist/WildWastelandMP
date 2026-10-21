# Merchant bus stock after hostility — 2026-10-21

Status: Testing. Denis requested that items released for free during hostility cost money again after hostility passes.

Native Buyable handles FactionCoyotesEnemy by enabling ordinary item use, releasing physics and setting ForBuyInt to zero without deducting money (Sources/fsm/prefabs/flashlight1.md:52). The faction manager returns to friendly after its native timer (Sources/fsm/scene1_game/__GameManager__.md:525). MerchantHost.ObserveStock previously treated every zero as a purchase, permanently removing the item from its stock tracking. Friendly recovery therefore could not restore its sale status.

Patrols now observes the existing ProcessEvent dispatcher for Buyable hostility events on its own tracked bus stock. A positive sale flag before the event marks a temporary hostility release. Paid native purchases do not use that event; multiplayer purchase reconciliation sends it only after applying zero, so it does not set the marker. Unclaimed items still parented under the bus remain tracked during hostility. When trading resumes, their saved price and native ForBuy state return. Items removed from the bus or claimed by a guest follow the existing permanent-removal path. No teleport or inventory reclamation occurs.

One optional boolean, hostilityReleased, is added to stock records within the existing MerchantSave JSON; absent values default false. Restoring a marked record preserves its free state while hostile or returns it to sale when friendly. No new save key, external storage, config setting, packet, handshake or version change. Already-purchased records from older saves cannot distinguish paid stock from historical hostility releases and remain untouched. This is limited to merchant buses; stationary native shops and the separately identified initial-publication/zero-price risks are unchanged.

Three Patrols source files, 33 additions/one removal. Windows Release/Deploy=false build passed with zero warnings/errors. Exact stage: V:/Repos/Apocalypter/_releases/WildWastelandMP_merchant-hostility_20261021a.dll. Built/staged MD5: 546A94EC1B24B2A83640E9D68928A6DC. Current installation has standard WW and no active MP folder: staged only. No game/harness/live save/config/base changes, commit, push or release.

## Denis's checks

1. With matching host/guest builds, visit a friendly bus. Buy one item as guest and leave another unpaid. Verify the buyer pays once and observers do not need to buy the paid item again.
2. Trigger Coyote hostility. Leave one released item on the bus; take another. Let the native hostility timer expire without further attacks. Remaining stock must show Buy with its previous price on host and guests. Paid and taken items must stay usable without a second payment.
3. Save/load while hostile with untouched released stock, then allow friendliness to return. Repeat saving after recovery and reconnect/late join. Confirm the stock remains for sale and previously taken items do not reappear.
4. With global VerboseLog enabled, recovery logs: `[Patrols] Merchant stock returned to sale after hostility: <item>`.

Runtime, persistence and multiplayer acceptance remain pending Denis's checks.
