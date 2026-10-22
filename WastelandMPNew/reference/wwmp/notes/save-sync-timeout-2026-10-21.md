# Bounded save synchronization (2026-10-21, Testing)

Denis requested saves continue after a pending-mod/owner timeout, with a yellow top-left warning naming the pending work and elapsed wait, also in the game log. Cancelling every attempt is explicitly not the requested behavior.

LogOutput (10).log lines 321–354 stay in the pre-write owner barrier; previous saves completed. The old 30-second limit cleared only player replies, while part/item/Ultimate Tools queues could block indefinitely. The log cannot identify which particular queue caused that incident.

MultiplayerSaving now gives both owner phases one shared 30-second unscaled-time synchronization budget. On expiry it proceeds with the last confirmed host state instead of waiting forever. If phase 1 times out, phase 2 is skipped. Pending queues are retained, not discarded or falsely acknowledged. Actual terrain preparation, serialization, transaction checks, resource-integrity failures and rollback protection remain in force. Unconfirmed changes may be absent from this snapshot.

The timeout log names the phase and actual elapsed seconds, missing player reply count (IDs in a separate log line), part queues/incoming updates/receipt counts, item-edit count, and Ultimate Tools initialization/requests/discovery roots/children/unapplied counts. A successful save queues the same warning after unfreezing, without replacing the completion status used by Save and Exit. CleanHUD displays it in yellow in a wrapping top-left row for 15 seconds; existing text-only fallback wraps it when CleanHUD is off. Guests receive/log the warning through the existing host notice packet. Only save-warning text may exceed the old 250-character notice cap (maximum accepted 1024); no new packet, handshake, config or save key. Older clients may discard warnings longer than 250 characters; update all peers for full diagnostics.

Windows Release/Deploy=false build succeeded with zero warnings/errors. Stage: `V:/Repos/Apocalypter/_releases/WildWastelandMP_save-sync-timeout_20261021a.dll`. Built/staged MD5: `A4B839F15783197AD4C550210C47F155`. Standard-WW installation currently lacks active MP folder/native dependency; staged only, no incomplete DLL-only deployment. No game launch, harness, live save/config/base edit, commit, push or release.

Denis's runtime checks:

1. Normal host/manual/autosave: completes normally without timeout warning; host and guests resume.
2. Reproduce the pending-operation save: after about 30 seconds of owner synchronization, it advances to terrain preparation/world writing. After success, a yellow top-left warning names what remained pending, counts, elapsed seconds and phase. Log includes `Save warning: waited ...` and eventual `Save completed:`/`Autosave completed:` plus `Server save: gameplay resumed`.
3. Guest: same warning and warning log entry; confirm last confirmed inventory/parts/tool state after reload. Check pending edits can still settle after gameplay resumes and the next save captures them.
4. CleanHUD off: yellow wrapping top-left fallback; normal message colors remain unchanged after the warning expires.
5. Save and Exit: successful timeout-assisted save still exits; actual failed writes retain existing failure handling. A warning is not a promise that unconfirmed operations made it into the snapshot.

The 30-second bound covers owner/mod synchronization, not the entire disk save; terrain has its separate existing timeout. Runtime/save-integrity acceptance remains Denis's.
