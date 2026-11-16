# ProjectEye Enhanced

- Fork of Planshit/ProjectEye: legacy C# / WPF, .NET Framework 4.5. Follow `PLAN.md` in phase order; passing a build is not passing runtime or OS compatibility acceptance.
- Preserve existing behavior, framework and dependency versions. Keep GSMTC PoC independent until its compatibility gate passes. Formal Release requires explicit user confirmation.
- Build entry: [baseline build guide](docs/BASELINE-BUILD.md). Use PowerShell 7 and full Framework MSBuild 17.8+; do not substitute dotnet build.
- Evidence and known risks: [Phase 0 audit](docs/PHASE0-AUDIT.md). Keep original failed evidence; never commit artifacts, caches, runtime Data/Log, `.codegraph` or `.recycle-bin`.
- Never permanently delete files. Move exact validated project targets to a unique `.recycle-bin` location. Do not run the original updater during validation because it permanently deletes files.
- Build in fresh isolated source copies; do not overwrite tracked Fody XSD or user configuration. Before runtime tests identify existing ProjectEye processes and the shared `projecteye` mutex; do not close the user's instance without authorization.
- RTK: noisy summary-oriented read-only checks only. Use native tools for exact logs, diffs and all mutations.
- CodeGraph: prefer for unfamiliar cross-file flow; no index currently exists. Use targeted rg when unavailable; do not commit graph data.
- Delivery: relevant build and runtime checks, exact diff review, `git diff --check`, final Git status and repository identity. Record manual vs automated evidence and untested environments separately. Commit each completed phase, without pushing or publishing.
