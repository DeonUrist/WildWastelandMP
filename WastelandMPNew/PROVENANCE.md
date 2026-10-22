# Provenance and independence

Prepared for Denis on 2026-10-22 by Codex. New runtime code in `src/` and `steam/` was written for this task from behavioral requirements and public framework/Steamworks APIs. It does not reference ApocalypterMP, reproduce its world-object implementation, carry its packet registry, or include its binaries/assets. Generic networking concepts such as acknowledgements, bounded windows, authority and lobby discovery are expressed in a new scoped architecture.

This was **not a formal clean-room process**: the author of this package reviewed ApocalypterMP source, the current WWMP integration and local Vistas compatibility evidence. Renaming someone else's implementation would not establish independence; this package instead separates source-review findings from the new implementation and records its dependencies. It cannot guarantee how another author will characterize the work.

## Evidence classes

| Material | Included? | Role |
|---|---|---|
| New kernel and Steam adapter source | Yes | Original implementation for this task; inspect commits/diff before eventual publication |
| Existing WWMP compatibility reports and project notes | Frozen Markdown copies | User project knowledge; may contain historical snippets/signatures, not new runtime implementation |
| User-pasted refactor documents | Frozen text | Requested planning context; newer local amendments take precedence |
| Current WW2 amended refactor plan/status | Frozen Markdown | Destination architecture and fresh-start rules |
| WWMP runtime implementation | File/hash inventory only | Original locations for future behavioral reference; not copied into the new kernel |
| ApocalypterMP reviewed source | File/hash inventory only | External source-review baseline; source bodies not redistributed |
| Vistas, EngineSim, ChaseCamera, other optional source/DLL/assets | No | Optional API/capability references only |
| Steamworks.NET, Steam runtime | No | Optional SDK dependency already supplied by the game; new adapter compiles against it |

No third-party license or redistribution permission is granted by this package. Existing project assets and frozen evidence retain their own authorship. In particular, do not copy ChaseCamera implementation/assets or optional-mod DLLs into a future release merely because they appear in compatibility reports.

## Audit trail

`REFERENCE-MANIFEST.json` records source path, bundled path, size and SHA256 for each of114 evidence documents. `SOURCE-INVENTORY.csv` records174 relevant WWMP source files. `NATIVE-REVIEW-INVENTORY.csv` records the external reviewed baseline. The checkout was dirty before this task: Vistas compatibility was pending and is marked as such, not attributed to this new work. Root HEAD at review was `6844d36` (RC18).

`tools/Export-Knowledge.ps1` reproduces the evidence extraction from local files. It copies only project Markdown/planning documents; native review contributes metadata/hashes only. The exporter never edits gameplay code or runs a game. Re-running it is an explicit snapshot refresh and may change evidence hashes; keep the date/source baseline in this document current when doing so.

No local Claude `claude/GAME_KNOWLEDGE.md` project document was available in this checkout. This extraction covers repository evidence and supplied/local refactor plans; it does not claim access to unprovided chats, private project memory, or a newer upstream native implementation.
