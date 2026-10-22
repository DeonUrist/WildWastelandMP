# Crew hiring and quest restrictions (Testing)

Quests 1.8.0 offers hiring and work without checking vehicle roles. A seat-only check also loses crew identity when Patrols replaces a dismount with a fresh native NPC. The approved fix separates hiring eligibility from quest-giver eligibility.

- Seated drivers, passengers and turret occupants cannot be hired or offer new quests.
- Former crew on foot can be hired under the original faction, reputation and hire rules, but cannot offer new quests. Current followers retain dismissal, and existing quests retain turn-in/return/abandon actions.
- New quest guards cover normal work offers, cached acceptance and scripted quest.give. Hiring is rejected before the original reputation charge.
- Patrols supplies runtime crew status through Services.FollowerVehicles.IsCrew. Ordinary bailouts and turret landings send the existing CrewBailedOut event with the replacement NPC's numbered native name. Names persist independently of NPC objects: no object retention, respawn, renamed NPCs or added save components.
- QuestsCompat saves the names in config/WildWasteland/QuestsCompat/Saves/SaveGameN.es3.crew.txt after the native save flush. It captures runtime crew before saving even if they have not been spoken to. Load selects that slot's record; starting a new world clears it. Existing sliced discovery identifies loaded runtime crew. Disabling the bridge restores the source's eligibility rules.

History begins with this patch. An NPC that already bailed out and was saved/reloaded before this patch may no longer have any crew-origin tag to identify; the fix cannot reconstruct that missing history. New dismounts and still-tagged survivors are tracked, including survivors never spoken to.

## Denis's in-game checks

1. Restart. Talk to a friendly driver, passenger and turret gunner: expect no Hire, Any work, or new quest acceptance options. Ordinary dialogue, trade and any existing quest turn-ins remain available. An ordinary NPC on foot still offers work/hiring according to the original rules.
2. Cause a friendly crew member to bail out, and a turret survivor to land, without killing them. On foot, expect hiring if normally eligible, but no new quests. Verify faction reputation is unchanged by any rejected hiring attempt while seated. Hired followers can still be dismissed, including while riding with the player.
3. Save without first speaking to a new survivor. Wait for saving to finish, reload, then talk to that same NPC: expect hiring on foot but no quest offers. Repeat with autosave and a turret survivor. Check the slot's .crew.txt file contains their numbered name.
4. Travel far enough to unload a survivor and return. If the game restores that same NPC, they still cannot offer quests. Permanent native distance cleanup may delete them; this patch does not bring them back. Load another save slot or start a new world: unrelated NPCs remain eligible.
5. If custom dialogue contains quest.give, use it on crew and a normal giver: crew must not receive a new active quest or delivery goods; ordinary givers retain the original action.

No config entries changed. Build verification is Windows Release/deploy only; gameplay and persistence verification remain for Denis. There are no new success trace lines; persistence errors log `[QuestsCompat] Could not load former crew` or `Could not save former crew`.

Validation: isolated checkout of RC8 plus this crew patch built with zero errors and seven existing warnings using the required Windows Release/deploy command. Concurrent ChaseCamera edits were left untouched after their compile error prevented a shared-tree build. Built/deployed DLL MD5: 0206E3C0F252D2DD870FA599FFC322F5. Snapshot: WildWasteland_0.1.11-persistent-crew-a.dll.

RC9 integrated release: the combined source including ChaseCamera built and deployed with zero errors and seven existing warnings. DLL MD5: 4D06742F7D2F6D02E92C54FA4F6F22C8.
