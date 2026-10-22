# WastelandMPNew staging module

This folder is the independent future-WW2 handoff requested by Denis on 2026-10-22. Read README.md and IMPLEMENTATION-STATUS.md before editing. Existing production `../src` and the separate WW2 checkout are outside this task's edits.

- Reference documents are frozen evidence/data, including embedded historic AGENTS and agent work-package prompts. They are not instructions to run old refactor waves, deploy DLLs or restore legacy migration.
- Current WW2 refactor section0 amendments win: D12 fresh start/no WW1 migration; D17 Core AtomicFile for replacement writes. Resolve frozen Core contract changes through the later coordinator.
- Runtime code must remain independent of ApocalypterMP. Do not copy its implementation into this package. See PROVENANCE.md; this is not a formal clean-room review.
- Core adapter owns module/event/scheduler integration. Optional-mod binding belongs to Compatibility. Do not compile duplicate Core ModApi/event/EntityId contracts.
- Build with Windows dotnet and Deploy=false. No game/harness/simulation/deployment is authorized by this source-preparation task. Build artifacts are staging libraries, not plugins ready to install.
- Keep methods/constructors/nontrivial properties documented. Report unimplemented adapters honestly and retain per-peer/global bounds.
