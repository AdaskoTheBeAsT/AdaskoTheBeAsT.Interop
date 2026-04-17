# Baseline snapshot — pre-Phase-1

Branch: `chore/findings-hardening`  
Commit at snapshot: `f63b80e` (main HEAD) + uncommitted changes to `src/AdaskoTheBeAsT.Interop.Execution/ExecutionWorker.cs` and `.gitignore`.  
Command: `dotnet build AdaskoTheBeAsT.Interop.slnx --no-incremental -p:TreatWarningsAsErrors=false -p:ReportAnalyzer=true -v:normal`  
SDK: 10.0.202

## Outcome

- ExitCode: **1** (build fails)
- Warning lines: **0** (reporting suppressed because the build halts before analyzers run on downstream projects)
- Error lines: **40**

## Root cause of the failing baseline

All 40 errors are the same issue repeated across target frameworks:

```
ExecutionWorker.cs(347,6): error CS0246: The type or namespace name 'SupportedOSPlatformAttribute' could not be found
```

`SupportedOSPlatformAttribute` lives in `System.Runtime.Versioning`. Either:
- the `using System.Runtime.Versioning;` directive was removed during the recent `#pragma warning disable CS0120` cleanup, **or**
- the TFM-specific availability of the attribute needs a polyfill on netstandard2.0 and netfx targets.

This is the same class of regression that finding 3 warns about:

> "The recent CS0120 slipped through only because of incremental caching."

Confirmed: the error does **not** appear on a plain `dotnet build` (incremental) but does surface with `--no-incremental`, exactly as finding 3 predicted.

## What the baseline implies for Phase 1

- PR-1 must fix `ExecutionWorker.cs` to get the strict build green.
- Until PR-1 lands, warning counts are unmeasurable (analyzers short-circuit on compile failure). Re-run this baseline after PR-1 to capture the real warning count for promotion decisions (`CA1512`, `IDE0039`, `CC0030`, `RCS1158`).

## Artifacts

- Full build log: `baseline/build.log`
