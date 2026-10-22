# Wild Wasteland / ApocalypterMP — active non-live checkpoint

**Step15, 2026-10-18: reviewed, patched, Windows Release built and tested with separate local host/guest processes.** The final WW test run passed45 host and48 guest checks, including8,300 impacts and replay rejection. Experimental / Testing; full gameplay acceptance remains open.

Read [STEP-15-REVIEW-AND-TESTS.md](STEP-15-REVIEW-AND-TESTS.md) for changes, exact test coverage, retained native-test failures and remaining limits. [RESUME.md](RESUME.md) identifies the working tree/package. Step1–14 reports are historical.

- Base:91bff52450e27a461fdbced48317910058ce5cf2, WW0.1.36 including QuestsCompat retirement.
- DLL MD5:C1836DE43A35BD24FFD094B91F865CE3; Windows Release Deploy=false, zero warnings/errors.
- Package:V:\Repos\Apocalypter\_staging\ww-mp-20261018\rc33-step15.
- Archive:WW-MP-step15-C1836DE4.zip. Complete patch/source overlay includes untracked files.
- No live deployment/config edits, commit or push. Installed WW DLL and16 baselined personal files unchanged. Test processes stopped.
- All peers need identical WW overlay and the inspected MP1.0/protocol21 DLL; handshake revision3. No third-party DLLs/assets or test-controller binary ship in this patch.

Host owns WW world/AI/events/patrols, quests/rep and world/fog/marker policy. Quest payout goes in full to every connected player's separate wallet. Guests retain HUD and map/compass display switches. Performance is off by default and locked off in MP; MP compass and debugF9–F12 are suppressed. Preserve alphas/revival/companionship/co-op vehicles/persistence/sleep votes/Steam; suppress MP lures/horn attraction/reinforcement waves. Native player-model choices and save identities remain available offline. QuestsCompat stays retired.

Review limits before the human server test: Steam/GPU/actual weapon input, full multi-player perception, optional EngineSim, every fitting/door/offline-load/weather case and exceptional resource refunds are not all validated by headless packet/fixture tests. Do not infer universal acceptance from the93 passing final checks.
