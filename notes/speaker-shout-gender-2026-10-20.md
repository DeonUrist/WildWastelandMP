# Speaker shout gender — 2026-10-20

Status: Testing. Denis approved the small cross-module/Core service change.

## Cause and change

`NpcModule.NetworkInput` receives guest shout requests (90) on the host and host relays (91) on guests. Both paths discarded the speaker identity before `Senses.ShoutAudio` called `Voice()`. `AnimationService.PlayerShout` forwarded to `FemalePain.PlayerShout`, which selected the listener's local Character. This proves the reported gender inversion in the source; the currently available game log is standard Wild Wasteland and has no MP shout reproduction trace.

The host request now passes the validated peer into `RemoteShout`, and receiving guests pass packet 91's existing source ID into `ShoutAudio`. The additive `IAnimationApi.PlayerShoutFor(ulong peer)` resolves the existing synchronized `MultiplayerAvatars.Choices` cache; it does not depend on a built/rendered remote body. Speaker Female, Gungirl and Shoota select the existing female clip, all other characters the male clip. Local playback uses the same character classification. Missing selection or unavailable WAV retains the existing native voice fallback, never the listener's local shout selector.

Six production files: Core/Services.cs; NPC/Detection/Senses.cs and MultiplayerPresentation.cs; PlayerBody/Animation/AnimationService.cs and MultiplayerAvatars.cs; PlayerBody/Body/FemalePain.cs. 17 added/nine removed lines. No new packet or payload, config key, save format, handshake, version, patch, scheduler job, asset or base-repo change. Existing cooldown, authority checks, self-echo exclusion, position conversion, volume and NPC hearing remain.

## Build and stage

- `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=false`: zero warnings/errors.
- Exact stage: `V:/Repos/Apocalypter/_releases/WildWastelandMP_speaker-shout_20261020a.dll` (79,977,984 bytes).
- Built/staged MD5: `8DA44A6EBBFF61A650E2A3A899D3C12D`.
- Not deployed: the current standard-WW installation has no MP asset folder or native Multiplayer dependency. Do not create an incomplete DLL-only installation.
- No game launch, harness, live config/save edit, commit, push or release.

## Denis's runtime checks

Use this identical DLL in complete MP installations on every peer, restart, and host/join normally. Existing strict build matching requires all peers to update.

1. Female guest shouts near a Male host: both hear the female clip, once each. Male host shouts back: both hear male.
2. Reverse host/guest character genders and repeat: playback follows the speaker in both directions.
3. With two guests of opposite gender, each shouts while the host and other guest listen: both listeners hear the speaker's gender; the sender hears only its local playback.
4. Switch character through the existing settings, wait for the normal selection sync, then shout. Include Gungirl/Shoota (female) and Merchant/Bodyguard/Sprokka/OrganicMechanic (male). Repeat after reconnect.
5. Check positional playback, volume, cooldown and nearby hostile NPC reactions still work; offline local shouting retains the correct character voice.

No new runtime log line was added. Existing verbose `Shout sound loaded: shout_male.wav` / `shout_female.wav` confirms WAV loading only; listening checks remain necessary for acceptance.
