# ProjectEye Enhanced

- Fork of Planshit/ProjectEye: legacy C# / WPF, .NET Framework 4.5. Follow `PLAN.md` in phase order; passing a build is not passing runtime or OS compatibility acceptance.
- Preserve existing behavior, framework and dependency versions. Keep media control modular. Windows 11 validation is deferred by the user (2026-11-16), so it does not block Phase 2; do not mark it tested. Formal Release requires explicit user confirmation.
- Build entry: [baseline build guide](docs/BASELINE-BUILD.md). Use PowerShell 7 and full Framework MSBuild 17.8+; do not substitute dotnet build.
- Independent GSMTC PoC: [Phase 1 guide and compatibility gate](docs/PHASE1-GSMTC.md); `./scripts/Build-GsmtcPoc.ps1 -Enumerate` builds to artifacts and performs read-only media enumeration. Only explicit `--pause` sends pause commands.
- Media module and tests: [Phase 2 contract](docs/PHASE2-MEDIA-CONTROL.md); `./scripts/Test-MediaControl.ps1 -Live` runs isolated net45 tests and read-only live enumeration. Rest integration: [Phase 3 contract](docs/PHASE3-REST-INTEGRATION.md), validated with `scripts/Test-AppIntegration.ps1` in isolated data. Media UI: [Phase 4 settings](docs/PHASE4-MEDIA-SETTINGS.md). Always run application tests with an isolated working directory as the legacy SQLite path is relative.
- Evidence and known risks: [Phase 0 audit](docs/PHASE0-AUDIT.md). Keep original failed evidence; never commit artifacts, caches, runtime Data/Log, `.codegraph` or `.recycle-bin`.
- Never permanently delete files. Move exact validated project targets to a unique `.recycle-bin` location. Do not run the original updater during validation because it permanently deletes files.
- Build in fresh isolated source copies; do not overwrite tracked Fody XSD or user configuration. Before runtime tests identify existing ProjectEye processes and the shared `projecteye` mutex; do not close the user's instance without authorization.
- RTK: noisy summary-oriented read-only checks only. Use native tools for exact logs, diffs and all mutations.
- CodeGraph: prefer for unfamiliar cross-file flow; no index currently exists. Use targeted rg when unavailable; do not commit graph data.
- Delivery: relevant build and runtime checks, exact diff review, `git diff --check`, final Git status and repository identity. Record manual vs automated evidence and untested environments separately. Commit each completed phase, without pushing or publishing.

- Comprehensive acceptance: [Phase 5 progress](docs/PHASE5-VALIDATION.md). Keep actual desktop, synthetic, unit, long-run and deferred OS results distinct; do not mark skipped fixtures as passed.

- User decision (2026-11-16): keep strict resume guards. Repeated Paused notifications invalidate this session's resume eligibility; pause-only degradation is accepted. Do not deduplicate notifications to improve resume rate or claim successful resume from accepted degradation.

- Desktop player evidence and exact-target test CLI: [player compatibility](docs/PLAYER-COMPATIBILITY.md). Missing SMTC registration is configuration-specific; never count zero target sessions as a successful pause test.
