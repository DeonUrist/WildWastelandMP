# Low Crawl asset authoring

Input: Denis's `C:/Downloads/Low Crawl.fbx`, copied unchanged to `Unity/Assets/LowCrawl.fbx`.
SHA256: `5B6062F769523180C7371D4893E43FE4334D1FFEB366A9E5DEEDD3076B501069`.

Run `./build-assets.ps1` with Windows PowerShell and installed Unity 2020.3.49f1. The headless Unity **Editor** imports one humanoid take, retains vertical pose and original orientation, makes a looping in-place clip with no foot IK, builds only the standalone AnimationClip for Windows64, and copies its bundle into PlayerBody/Embedded/Crawl. The normal WW DLL build embeds it. No game process, runtime test or game-folder assets are involved.

2026-10-19 asset build succeeded: valid humanoid avatar/clip, 30 Hz, frames 0–70, duration 2.333333 seconds, bundle 160,671 bytes. This checks asset import and packaging only; runtime retargeting, appearance, contact and performance remain Denis's checks.
