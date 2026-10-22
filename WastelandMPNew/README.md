# WastelandMPNew

An independent multiplayer kernel and integration handoff for Wild Wasteland 2.0. Prepared for Denis on 2026-10-22. Status: **buildable foundation; gameplay integration and runtime verification pending**.

The package lives outside the active `src/` tree. It does not register a BepInEx plugin, patch ApocalypterMP, load a game, change a save, or install anything. It has its own protocol and does not connect to existing WWMP/ApocalypterMP servers.

## Start here

| File | Purpose |
|---|---|
| [REVIEW.md](REVIEW.md) | Source review of native MP, UDP hosting and Steam hosting; evidence and limits |
| [COMPATIBILITY.md](COMPATIBILITY.md) | Consolidated WW multiplayer rules and optional-mod coverage |
| [INTEGRATION.md](INTEGRATION.md) | Exact boundaries for Core ModAPI, future module adapter and WW2 integration order |
| [PROTOCOL.md](PROTOCOL.md) | Envelope, streams, lifecycle, validation and resource budgets |
| [IMPLEMENTATION-STATUS.md](IMPLEMENTATION-STATUS.md) | Implemented code versus work that still needs WW2 services |
| [VERIFICATION.md](VERIFICATION.md) | Build evidence and Denis's eventual runtime/performance checks |
| [PROVENANCE.md](PROVENANCE.md) | Original implementation, reviewed external code, dependency and authorship boundaries |
| [REFERENCE-INDEX.md](REFERENCE-INDEX.md) | 114 frozen evidence documents, including the supplied plans and newer WW2 amendments |
| [SOURCE-INVENTORY.csv](SOURCE-INVENTORY.csv) | 174 WWMP source locations with hashes; existing code remains in its original tree |
| [REFERENCE-MANIFEST.json](REFERENCE-MANIFEST.json) | Origin, byte count and SHA256 of each evidence document |

## What the sources do

- Authenticate a provisioned UDP endpoint with per-peer session keys and distinct direction keys. Supply a Steamworks.NET lobby/P2P adapter using the game's existing Steam runtime.
- Pace independent ordered control/world/combat streams with fixed windows, selective receipts, retries and explicit producer backpressure. Deliver complete superseding visual bundles without reliable queuing.
- Track admission milestones, host-assigned ownership revisions, launch selection, snapshot completion, request deduplication and bounded save synchronization.
- Validate fixed envelopes before decoding and host/owner permissions before gameplay dispatch. Keep host settings in a read-through overlay instead of writing guest preferences.
- Express Vistas ownership decisions as pure policies; optional API binding belongs to WW2 Compatibility.

The code **does not yet replicate native objects, inventory, vehicles, combat, quests or sleepers**. Those need the WW2 event codecs, snapshot participants and engine adapters specified in the handoff. This is useful integration source, not a finished replacement MP release. Optimization claims are structural (fixed buffers, explicit budgets, no scene scans/reflection in this kernel); no FPS or latency improvement has been measured.

## Build on Denis's Windows machine

```powershell
dotnet build WastelandMPNew/WastelandMPNew.csproj -c Release -p:Deploy=false
dotnet build WastelandMPNew/steam/WastelandMPNew.Steam.csproj -c Release -p:Deploy=false
```

Both target C# 7.3 / .NET Framework 4.7.2 using the game's managed reference assemblies. Override `-p:GameDir=...` for another installation. No package downloads, compiler installation or ApocalypterMP DLL are required. The Steam project references only this kernel and the game-provided Steamworks.NET assembly, which is not copied into the package.

## Plan precedence

The local WW2 plan has newer D11–D17 amendments than the pasted plan: **fresh games and configs, new module asset folders, no WW1 migration**, and all replacement writes through Core `AtomicFile.Replace`. See [the captured amended plan](reference/ww2-refactor/README.md). Old compatibility reports retain historical save/config details to explain behavior; they do not authorize reintroducing migration code into WW2.

## Hand this to Claude/Codex

> Read WastelandMPNew/README.md, REVIEW.md, COMPATIBILITY.md, INTEGRATION.md, PROTOCOL.md, IMPLEMENTATION-STATUS.md and the current WW2 docs/refactor/README.md section 0. Integrate this original kernel after W0 contracts are frozen. Use Core ModAPI exclusively across modules and put optional-mod binding in Compatibility. Implement the explicitly missing gameplay adapters and acceptance gates; do not substitute the old ApocalypterMP patches or claim the foundation is finished multiplayer. Preserve current WW2 save transactions and the fresh-start decision. Build only; Denis performs runtime checks. Do not deploy, commit or publish unless requested.
