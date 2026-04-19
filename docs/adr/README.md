# Architecture Decision Records

This folder captures small, self-contained design decisions taken on the `AdaskoTheBeAsT.Interop.Execution` codebase.

Each record follows the same shape:

- **Context** — why the decision was needed, what was on the table.
- **Decision** — what was actually chosen, with concrete code / API impact.
- **Consequences** — trade-offs, follow-ups, and forward-looking caveats.
- **Benefits** — the scoring / quality payoff, so the reason for the change stays traceable.

## Status legend

| Marker | Meaning |
| --- | --- |
| `Accepted` | Landed in the current main-line codebase. |
| `Proposed` | Written down but not yet implemented. |
| `Superseded by NNNN` | Replaced by a later ADR. |

## Index

| ADR | Title | Status |
| --- | --- | --- |
| [0001](0001-retain-test-only-hooks.md) | Retain test-only hooks instead of deleting them | Accepted |
| [0002](0002-pluggable-worker-scheduler.md) | Pluggable `IWorkerScheduler<TSession>` seam with two built-ins | Accepted |
| [0003](0003-public-diagnostic-constants.md) | Public `ExecutionDiagnosticNames` constants | Accepted |
| [0004](0004-valuetask-hotpath-overloads.md) | `ValueTask` hot-path extension overloads | Accepted |
| [0005](0005-public-integration-test-project.md) | Public integration test project | Accepted |
| [0006](0006-nuget-packaging-metadata.md) | NuGet packaging metadata and Source Link | Accepted |
| [0007](0007-zero-alloc-value-task-source.md) | Zero-allocation `ExecuteValueAsync` via pooled `IValueTaskSource<T>` | Accepted |
| [0008](0008-uniform-snapshot-surface.md) | Uniform `Name` / `GetSnapshot` surface and single-factory pool constructor | Accepted |
| [0009](0009-scoped-execution-diagnostics.md) | Scoped `ExecutionDiagnostics` with `Shared` singleton | Accepted |

## Scope

These ADRs cover the quality-score delta taken after the Phase 0 → Phase 3 hardening described in [`../../plan.md`](../../plan.md) and the residual gaps listed in [`../../improve.md`](../../improve.md). Larger architectural moves (initial worker design, `IAsyncDisposable`, options pattern, DI split) are described in `plan.md` itself and are not duplicated here.

## Adding a new ADR

1. Copy the structure of the most recent entry.
2. Use the next free sequence number (zero-padded, four digits).
3. Link the new ADR from this index and reference it from `README.md` or code comments where appropriate.
4. Never rewrite an accepted ADR in place — if you change your mind, add a new ADR and mark the old one `Superseded by NNNN`.
