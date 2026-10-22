# Apocapocket / CleanHUD compatibility preparation

Investigated 2026-10-15 against WW HEAD `d578ec1` and local Apocapocket 2.0.10 sources. Denis approved implementation and build/deploy. Status: Testing; runtime acceptance remains Denis's task.

Windows `dotnet build WildWasteland.csproj -c Release -p:Deploy=true` succeeded with zero errors and seven existing warnings. The build output and deployed `E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\WildWasteland\WildWasteland.dll` both have MD5 `566895CD3298E0929EFD8B3F22A1DACE`. About 240 changed code lines across ApocapocketInventory.cs, Bindings.cs, Hud.cs, Keys.cs, CleanHudModule.cs and the single Core Plugin.cs attribute; affected docs/knowledge rows updated. No config changes, game execution, harness, commit or push.

Release follow-up: Denis requested push/release. Numeric version 0.1.6 / RC3 rebuilt and deployed with zero errors and seven existing warnings; built/deployed DLL MD5 `71B9626142848EA7C0AD4A82A6CC6D66`. The release carries this integration and all RC2 assets; the unrelated Talk/Quests plan is excluded. Testing status remains unchanged.

## Evidence and gaps

- CleanHUD `Bindings.Item/Selected` reads three native holders/FSM flags; `Hud.BuildSlots/TickSlots` builds only F1, F2 and slots 1-3.
- Apocapocket `Runner.Slots[0..5].Content` is the inventory authority. `BeginBorrow` parks the native host's content and moves an extra weapon into that host without changing the logical model. Native holder/FSM reads consequently identify the borrowed weapon as the host slot and miss the parked item.
- Ordinary-item retrieval clears `Slots[target]` after recording `_handOrigin` and `_handName`; `HeldItem()` supplies the held object. Reading `Selected` plus `Slots` alone loses the selected item's icon. PlayerBody `EquipmentInventory` already reconstructs this case with compiled reflection delegates; use that mechanism locally without referencing PlayerBody types.
- CleanHUD `Assets.Describe` classifies an unknown non-Reload object as MELEE. A pocketed ordinary item needs a separate item presentation rather than this weapon fallback.
- Apocapocket creates cloned widgets ending in `_apocapocket`, decoration clones and `Apocapocket.CardExtension` in LateUpdate. CleanHUD captures replaced graphics once. Newly created graphics must also be captured for alpha hiding and later restoration; do not disable inventory objects/FSMs.
- CleanHUD `Keys.AddGame` already discovers `Item 4/5/6` from InputStorage and assigns them to WEAPONS. `AddMods` discovers Apocapocket's separate keys.cfg through ConfigEntry fields. Follow-up inspection of `Keys.Norm` confirms that Item4Key / Item4AltKey and Item 4 already share a mirror; the initially suspected self-conflict gap needs no change.
- HUD key badges are hardcoded, so displaying the actual primary bindings needs cached InputStorage reads and badge updates only when bindings change.
- WW `Plugin.cs` lacked an Apocapocket soft dependency despite the documented convention; one attribute now orders bridge resolution after its Awake.
- The reachable LogOutput.log shows CleanHUD attached, but no Apocapocket load entry; no Apocapocket DLL was found beneath the installed plugins directory. Runtime coexistence is not demonstrated by this log.

## Implemented scope

1. Add `src/Modules/CleanHUD/ApocapocketInventory.cs`: optional, reflection-only bridge with NoInlining entry points and compiled delegates, no Apocapocket DLL reference. Bind Runner Instance/Ready/Slots/Selected/Unlocked/CurrentOp/Save, SlotState Content/Kind, held-item identity, and Enabled. Missing/unsupported plugin falls back to the existing three slots with one diagnostic.
2. Read one stable logical snapshot per HUD tick. Retain the previous snapshot during inventory operations, load restoration and save normalization. Reconstruct held items; do not interpret physical parent changes as logical changes. Clear cached snapshots on scene/load/module teardown.
3. Extend `Bindings.cs` and `Hud.cs` for 3-6 inventory entries, show only unlocked entries, and highlight the logical selection. Fit the extra entries inside the existing configurable slots group by reducing item pitch/width as capacity grows; keep F1/F2 and the group's placement/scale controls. Do not expand the centered row into the vitals/weapon panels. Primary-key badges update once per second, resizing their existing borders/text only when keys or capacity change.
4. Use CleanHUD's existing weapon textures for weapons. For ordinary items reuse Apocapocket `Icons.IconFor` through a compiled delegate on content changes, with bounded retries for queued textures; do not invoke its hierarchy/name/cache work every frame. Show an item label/icon with weapon ammo fields hidden. Apocapocket owns borrowed textures and retains responsibility for generating its item icons.
5. Capture newly created replacement graphics through an optional postfix on `ExtraSlots.BuildUi`, covering cloned roots/decorations/card extension. Keep alpha restoration symmetrical with CleanHUD disposal; retain existing canvas render hiding for late writes. Apocapocket's nearby-widget scan can capture CleanHUD's own panels: restore those records' original positions and remove only those records from its movement list. No changes to Apocapocket source, input, transactions, saves or native widget geometry.
6. Keep native action names intact; show Item 1-6 labels while enabled and reflect primary key changes. Preserve the existing action/config mirror handling, alternative-key rebinding and actual conflict reporting.
7. Add one soft-dependency attribute in `src/Core/Plugin.cs`; update only affected compatibility/patch rows in docs and the repo knowledge bullet. No new config entries or save data. No other module changed.

## Verification and handover after implementation

- Build/deploy once using `dotnet build WildWasteland.csproj -c Release -p:Deploy=true`; require zero errors and report matching built/deployed DLL md5. If DLL copy is locked, stop and ask Denis to close the game. No game launch, harness, commit or push by the agent.
- Denis: enable Apocapocket 2.0.10, load a save, and check 3 slots without a backpack, 4 with small, 5 with medium, and 6 with large/huge; RequireBackpack off should show 6. F1/F2 must remain visible and the row must not overlap adjacent panels.
- Fill slots 1-3, put a different weapon in 4-6, draw/holster/switch it. Expect the logical extra slot highlighted, all original slot icons retained, and the drawn weapon's correct caliber/ammo.
- Store/retrieve an ordinary item, switch away, drop it, and return it to a slot. Expect correct item icons, a held-item indication, and no MELEE/ammo presentation for ordinary objects.
- Rebind primary/alternative Item 4-6 controls in CleanHUD, resume, reopen controls and restart. Expect persisted input and updated primary badges, no self-conflict warning, and real conflicting actions still reported.
- Remove/downsize the backpack, enter/leave a vehicle, hide/show HUD, save/load, toggle CleanHUD off/on, then restart without Apocapocket. Expect native overflow/input/save behavior, no duplicated slot widgets, proper native UI restoration, and the three-slot fallback.
- For any failure, enable Wild Wasteland Enable Debug and VerboseLog plus Apocapocket Debug/VerboseLog, reproduce the exact operation once, then provide LogOutput.log. Check both plugin load entries and `[CleanHUD] Apocapocket inventory bridge ready (2.0.10)`; unavailable/read-failure diagnostics explain fallback. Runtime coexistence has not been tested by the agent.
