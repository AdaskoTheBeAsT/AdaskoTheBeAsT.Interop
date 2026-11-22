# 0021. Share hosted STA pool configuration

**Status:** Accepted in 3.1.0

## Context

Hosted integrations repeat the bounds for worker count, queue capacity, execution deadlines, and session recycling. Their STA pools also repeat the same scheduling and shutdown policy.

## Decision

Expose opt-in `ExecutionPoolConfiguration` helpers in Execution.Hosting. The caller supplies its worker limit, uses the validator with its own options, and applies the STA preset in the existing pool callback.

## Consequences

Existing registration overloads and defaults are unchanged. Configuration keys, diagnostics names, lifecycle hooks, stack size, and the deadline passed to each calculation remain the caller's responsibility.
