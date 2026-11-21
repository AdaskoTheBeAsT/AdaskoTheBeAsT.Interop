# ADR-0011 — .NET 11 target framework matrix and 3.0.0 dependency baseline

- **Status**: Accepted
- **Date**: 2026-11-16
- **Related**: [2.x to 3.0.0 migration guide](../migration-3.0.0.md), [ADR-0006](0006-nuget-packaging-metadata.md)

## Context

`2.2.0` targeted six frameworks (`net10.0`, `net9.0`, `net8.0`, `net481`,
`net48`, `net472`) on `.NET SDK 10.0.x` with `Microsoft.Extensions.*` `10.0.x` /
`9.0.x` / `8.0.x` conditional package ranges. With the release of `.NET 11`
(`11.0.100`) and C# 14:

1. **Out-of-band / older modern TFMs added maintenance and CI matrix weight.**
   Supporting four simultaneous modern `.NET` targets (`net8.0` through
   `net11.0`) alongside three Windows desktop `.NET Framework` targets (`net472`,
   `net48`, `net481`) would expand every build and test run to 28 project-target
   combinations while downstream services standardize on `.NET 10` LTS and
   `.NET 11`.
2. **`.NET Framework` interop hosts still require first-class support.**
   Desktop and Windows Server / Nano COM and native-DLL workloads continue to
   target `net472`, `net48`, and `net481` and consume current
   `Microsoft.Extensions.*`, `System.Threading.Channels`, and
   `System.Diagnostics.DiagnosticSource` `11.0.0` packages.
3. **Removing target frameworks is a SemVer major breaking change.** Dropping
   `net8.0` and `net9.0` requires bumping `<Version>` from `2.2.0` to `3.0.0`
   even though public C# APIs on retained targets remain source- and
   binary-compatible.

## Decision

1. Pin `global.json` to `.NET SDK 11.0.100` (with `Microsoft.Testing.Platform`),
   set `<LangVersion>14.0</LangVersion>`, and bump `<Version>` in
   `Directory.Build.props` to **`3.0.0`** across all three shippable packages.
2. Standardize all three `src/` packages (`Execution`,
   `Execution.DependencyInjection`, `Execution.Hosting`) and all four `test/`
   projects on the **five-target matrix**:
   - `net11.0`
   - `net10.0`
   - `net481`
   - `net48`
   - `net472`
3. Align conditional package version ranges:
   - `net11.0`: `Microsoft.Extensions.*` `[11.0.0, 12.0.0)`.
   - `net10.0`: `Microsoft.Extensions.*` `[10.0.15, 11.0.0)`.
   - `net481`, `net48`, `net472`: `System.Threading.Channels`,
     `System.Diagnostics.DiagnosticSource`, and `Microsoft.Extensions.*`
     `[11.0.0, 12.0.0)`.
4. Preserve C# source and binary compatibility on all five retained targets —
   new `3.0.0` capabilities ([ADR-0012](0012-contextual-session-lifecycle.md),
   [ADR-0013](0013-before-thread-start-hook.md),
   [ADR-0014](0014-execution-deadline-extensions.md),
   [ADR-0015](0015-hosting-health-checks-and-readiness-snapshots.md)) are added
   via derived interfaces, optional properties, constructor overloads, and
   extension methods rather than mutating existing abstract contracts.

## Consequences

- **Breaking for `net8.0` and `net9.0` consumers.** Applications targeting
  `.NET 8` or `.NET 9` must retarget to `net10.0` or `net11.0` before upgrading
  to `3.0.0`, or remain on `2.2.0`.
- **Higher package floors on `net10.0` and `.NET Framework`.** `net10.0`
  consumers resolve `Microsoft.Extensions.*` `>= 10.0.15`; `.NET Framework`
  consumers resolve the `11.0.0` out-of-band Microsoft packages.
- **No forced code churn on retained targets.** Existing `2.2.0` session
  factories, schedulers, worker/pool constructors, options, snapshots, and
  DI/Hosting calls compile and run unchanged.

## Benefits

- Keeps the multi-targeting matrix lean (5 TFMs across 7 projects) while
  covering current STS (`net11.0`), current LTS (`net10.0`), and supported
  Windows `.NET Framework` (`net472`–`net481`).
- Unlocks C# 14 across the codebase and test suite under a deterministic
  `.NET SDK 11.0.100` toolchain.
