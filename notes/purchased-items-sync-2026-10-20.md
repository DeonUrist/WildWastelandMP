# Purchased item interaction synchronization — 2026-10-20

Status: Testing. Denis reports a guest bought a flashlight, then the host still had to buy the same physical item; suspects other purchased items too. Source investigation found a concrete reconciliation omission in WWMP's item-edit bridge. No runtime reproduction was performed by the agent.

## Cause

Native `Buyable/buy` deducts money locally, enables normal ItemName/use behavior, releases the item physics/layer, sets `ForBuy.ForBuyInt=0`, then disables Buyable. `ForBuyInt` is an existing saved/networked item variable. See `V:/Repos/Apocalypter/Sources/fsm/prefabs/flashlight1.md`, Buyable/buy and saveItemVar/load; backpack_small, first_aid_1, old_knife and ammo_box_9mm use this same purchase pattern.

The inspected native ApocalypterMP `WorldObjects.ApplyVars` captures the previous sale flag, applies the snapshot and sends `FactionCoyotesEnemy` to an enabled Buyable FSM when the flag changes from positive to zero. Despite its name, this existing event is also the native MP purchase-reconciliation route: it runs the local release transition without replaying the money deduction. It enables item-specific use behavior too (for example Ammo on ammunition).

WWMP `MultiplayerItems.BeforeSendVars` routes nearby edits on items owned elsewhere through `MultiplayerEdits` instead of allowing unrestricted native snapshots. Guest purchases of host-owned shop stock take this path. Its owner edit, host completion and guest snapshot calls all use `MultiplayerItems.Edit`. That method directly wrote both variables and their baselines, bypassing native `ApplyVars`. Thus the synchronized flag could be zero while Buyable stayed enabled and ItemName/use stayed disabled. Later native snapshots also cannot repair this particular state: `ApplyVars` sees the prior flag already zero, so its positive-to-zero test fails. This explains a second Buy prompt despite the purchase flag being synchronized. This omission is in WWMP's custom bridge; the inspected ApocalypterMP baseline already includes its own purchase reconciliation.

## Change

One production file: `src/Core/MultiplayerItems.cs`.

After the entire edit payload is validated and all fields/baselines are applied, detect an edited FsmInt named ForBuyInt with value zero. Verify its identity matches the root's actual ForBuy.ForBuyInt (not another similarly named saved variable). If the root's Buyable FSM remains enabled, send the same native reconciliation event before publishing the result. This covers authority-owner application, host commit and every mirrored guest snapshot through the existing shared method, including repeated zero snapshots with a stale Buyable interaction. Already-disabled interactions are skipped. No flashlight/item-name list, new variable, packet, patch target, job or config is needed. Existing proximity, owner, epoch, revision, receipt and payload checks remain. No native buy state or Money action is invoked.

Global VerboseLog records `[Multiplayer] Purchased item interaction reconciled: <item>` when an enabled interaction is reconciled. Native buyer payment remains unchanged. Simultaneous purchase/payment arbitration is outside this change; this fixes the stale remote interaction after a completed purchase.

## Build and stage

Windows: `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=false -p:OutputPath=bin/Release/purchased-items_20261020/ -v:minimal` passed with **zero warnings/errors**. Source diff whitespace check passed (only repository line-ending notices).

Exact stage: `V:/Repos/Apocalypter/_releases/WildWastelandMP_purchased-items_20261020a.dll`. Built/staged MD5: **1FEFF273B0151FA97C9E6C596FB667B9**. Includes the pending occupant collision protection and autosave-warning/log fixes. Version remains0.1.45, handshake24.

Current installation contains standard WW and no MP folder/dependency, with Apocalypter PID45628 running: staged only. No game/harness launch, deployment, live config/save edits, base/external DLL changes, commit, push or release.

## Denis's checks

1. Restart host and guests with this build. Guest buys a flashlight and leaves it in the world. Host and a second guest should see Take/use rather than Buy and should not pay again. Wait several native item ticks and then pick up/drop/equip it on another peer.
2. Repeat with backpack, weapon, first aid, ammunition and vehicle part, at stationary shops and traveling merchants. Confirm normal item-specific use is enabled (including taking ammunition), unpaid stock still requires payment, and insufficient funds does not release an item.
3. Reverse buyer/observer roles, including host buying stock owned elsewhere. Buyer pays the normal price once; observing peers' balances should not change from reconciliation.
4. Save/reload and reconnect/late join after purchase; verify the existing sale flag persists. Check repairs/refills and other edited variables still synchronize and the save barrier completes.
