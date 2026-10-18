# Active checkpoint — WildWastelandMP step16

Work moved to V:\Repos\Apocalypter\WildWastelandMP at Denis's explicit request. Read STEP-16-PLAYER-PRESENTATION.md and the root README-MULTIPLAYER.md. The former isolated worktree and step15 archive are historical checkpoints.

Project: WildWastelandMP.csproj. Installed game deployment is now authorized. Close both host/client processes before Deploy=true; do not kill them. Latest deployed MD5 D76A82E140960CAD1F4FECCBD588994B, zero warnings/errors. No commit/push. Save keys, namespaces and plugin GUID remain unchanged.

The graphical BAT is installed in the repo and E:\SteamLibrary\steamapps\common\Apocalypter. It starts local UDP host/client. Rehosting within an already running window needs F6. Native SteamAPI initialization errors do not prevent local UDP. New presentation and socket lifecycle changes still need human runtime validation; previous step15 suite results do not certify this build.

Never discard untracked Core/module compatibility files. Original WildWasteland main remains untouched. Current user is testing interactively; don't restart or stop their game without coordinating required file-lock/verification steps.