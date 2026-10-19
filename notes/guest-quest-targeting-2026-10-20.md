# Guest coyote quest targeting — Testing

Release follow-up: Denis requested push/release. RC9 / 0.1.45 uses the same targeting fix; final DLL MD5 `04BC690174D31569EDC8F6C2F90C580B`, stage `V:/Repos/Apocalypter/_releases/WildWastelandMP_0.1.45-RC9a.dll`. See release-0.1.45-RC9.md for package evidence. The unversioned diagnostic stage below is historical.

The native Multiplayer ApplySpawn disables creature PlayMaker FSMs on guest replicas. PlayMakerFSM.OnDisable removes those FSMs from FsmList. QuestWorld built People only from that list, so FindInteraction never saw guest coyotes. The existing client conversation requests already execute quest choices on the host; quest access was not intended to be host-only. The native WW interaction uses a four-metre horizontal range and dot-product threshold .85, not a small collider hit target.

QuestWorld.cs adds 17 lines: retain guest creature Health components from the existing network spawn notification, discover already-present MP replicas through the existing scene walk, merge them into People at publication, prune destroyed/untracked objects, and clear/unsubscribe with the existing lifecycle. Creature root identity is checked through the existing bridge. NPC simulation stays disabled. No new job, patch, packet, setting, save format or base-repo edit.

Source evidence: `src/Modules/Quests/QuestWorld.cs`, `NativeConversation.cs`, `PartyInteractions.cs`; existing external `_staging/ww-mp-20261018/inspection/ApocalypterMP.decompiled.cs` ApplySpawn; `Sources/code/PlayMaker/PlayMakerFSM.cs` OnDisable.

Windows `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=false` passed with zero warnings and errors. Exact stage: `V:/Repos/Apocalypter/_releases/WildWastelandMP_guest-quests_20261020a.dll`; built/staged MD5 `A551B62BACA5D17934EB65010D58475E`. The local game currently has standard WW without the MP folder/dependency, so no DLL-only deployment was made. No game or harness launched; current local log is a standard-WW session, not a guest reproduction. No live configs/saves changed and no commit/push/release.

Denis's manual checks with the matching build on host and guests:

1. Approach a friendly living camp coyote on foot within four metres, face its torso and press the configured Quests talk key (default F). Expect the Talk prompt and conversation choices.
2. Choose Any work for me, accept an offer, and verify the shared quest appears on host and guests. Close/reopen and check report-in/abandon choices.
3. Repeat after travelling to newly spawned NPCs and after reconnecting. Retest host interaction and an offline world.
4. Check dead, obstructed and out-of-range NPCs do not become valid interactions. Existing host validation still rejects invalid NPCs/choices; crew quest restrictions remain the existing policy.

No new diagnostic log line is added. Prompt, dialogue and shared-journal behavior provide acceptance evidence. Optional Apocatalker continues to own its own targeting when installed; this correction concerns WW's built-in interaction cache.
