# WW2 refactor - coordinator status

Repo: cloud `/home/claude/ww2` (branch `refactor/main`) mirrored to `V:\Repos\Apocalypter\WildWasteland2`. Plan: `docs/refactor/README.md` (section 0 amendments first), `work-packages.md`.
Imported from WildWasteland HEAD 2e98d0b (WW 1.0.9) on 2026-10-22. Baseline compile: OK (39 warnings).

## How to resume
1. Read this file, then `docs/refactor/state/<package>.md` of any package marked in progress.
2. Worktree per package: `git -C /home/claude/ww2 worktree add /home/claude/wt/<package> -b refactor/<package> refactor/main` (or reuse the existing branch: `worktree add /home/claude/wt/<package> refactor/<package>`).
3. Start the agent with its brief from work-packages.md + "resume from docs/refactor/state/<package>.md".
4. Merge finished branches into refactor/main (`git merge --no-ff refactor/<package>`), run `sh tools/refactor/compile.sh`, update this file.
Reference DLLs for compile.sh: copy `Apocalypter_Data/Managed` and `BepInEx/core` from the game folder to `/home/claude/refs` (cloud session), or set MANAGED/BEPCORE.

## Waves
| wave | package | branch | state | merged | notes |
|---|---|---|---|---|---|
| W0 | W0-A Core kernel | refactor/W0-A | not started | | |
| W0 | W0-B Core utilities | refactor/W0-B | not started | | |
| A | A1..A8 | | not started | | |
| B | B01..B18 | | not started | | |
| C | C | | not started | | |

## Open questions for Denis
(none)
