# Issue #2 — foot traveller hearing (Testing)

2026-10-18. Scope: WildWastelandMP issue #2, plus the same source fix in base WildWasteland at Denis's explicit request. Base receives repo changes only.

## Cause and evidence

The live host log registered Gungirl(Clone)255, Scrud(Clone)256 and Lugnut(Clone)257 as Scrapyard NPCs hostile to Player, at full daylight sight range (100 m), then repeatedly gave them travel orders without acquisition. Denis confirmed they attacked after he stepped into their forward cone. This establishes working visual acquisition; the code explicitly excluded simple bandits from Noise, Assign and the gunshot responder pool. Their separate gunshot shortcut granted live fought-memory without visual acquisition.

## Change

In each repo, NPC/Detection/Senses.cs and NPC/Brain/Brain.cs have 7 added / 8 removed lines total. Active dismounted simple bandits now enter normal sound investigation and search, with ordinary sound ranges, idle-engine attenuation, faction/storm checks, priority, gunshot responder limits and expiry. The existing Brain path walks to sound ghosts and looks around; visual combat returns to the existing simple combat path. The fresh-shot fought-memory shortcut is removed; hearing does not acquire a hidden shooter. Existing combat memory renewal remains. No config, save, protocol or external DLL changes.

## Build and deployment

- Base: `dotnet build WildWasteland.csproj -c Release -p:Deploy=false -v:minimal`, zero warnings/errors. No base deployment, as requested.
- MP: `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=true -v:minimal`, zero warnings/errors, both game processes closed first.
- Built, staged and installed MP MD5: `790DB68C3F407C5BB83FB6FE104EE358`.
- Unique stage: `bin/Release/WildWastelandMP_issue-2-hearing_20261018_160450.dll`.
- Installed: `E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\WildWastelandMP\WildWastelandMP.dll`.
- Concurrent unrelated MP source edits are retained in the shared build. No game launch, automated gameplay test, commit or push.

## Denis's runtime retest

1. Keep host NPC VerboseLog enabled. Approach unalerted Scrapyard foot travellers from behind or beside them with both players in the running car, within roughly 20 m; rev if needed. They should interrupt travel and investigate the engine, then fight only after seeing a player. A silent car alone must not attract them by sound.
2. Use a fresh/unalerted group, stop the engine and fire from outside their cone/behind cover without hitting them. Eligible responders should investigate the gunshot location, rather than immediately track an unseen player. Move silently while hidden to check last-known-point behaviour. Normal responder limits mean not every distant listener must investigate.
3. Stop making noise and stay concealed: investigation should finish and travel resume after the normal search. Enter their view to confirm normal combat still works. Repeat with a guest-owned car and guest gunfire.
4. Base gameplay verification uses its own later standalone session, not both plugins together.

Confirm named `Senses: <traveller> investigates Engine at ghost #...` / `investigates Gunshot at ghost #...`, normal `Engine (...) ... alerts ...` / `Gunshot (...) ... alerts ...`, and Brain ghost/search transitions. Gameplay remains unverified until Denis retests; leave the issue open/Testing.
