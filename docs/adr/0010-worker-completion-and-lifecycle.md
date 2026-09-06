# ADR-0010 - Worker completion ownership and lifecycle

- **Status**: Accepted
- **Supersedes**: [ADR-0007](0007-zero-alloc-value-task-source.md)

## Context

The pooled work items completed callers from inside delegate execution. A
consumer could reset and return a source while the worker still inspected its
options or reported teardown errors. Pooled delegate exceptions also bypassed
the worker's failure/recycle path. Startup, fault publication, repeated disposal,
and hosted deadlines did not consistently match their documented contracts.

## Decision

### Completion ownership

Task and pooled ValueTask work items stage results without publishing them.
The worker applies cancellation, session lifecycle policy, and diagnostics first.
Publishing result, exception, or cancellation is its **last access** to the item.
Only a consumer observing a completed source may reset and return it.
Pending or stale-token `GetResult` calls throw without recycling the source.
ValueTasks still require exactly one observation; defensive checks do not make
multiple awaits supported.

Pooled sources and instance `ExecuteValueAsync` overloads are available on all
nine supported TFMs, using the task-source compatibility package on .NET
Framework. There is no public extension-method fallback. Pooling avoids the
per-request source/Task allocation after warm-up, not all possible channel,
tracing, continuation, or cold-start allocations.

### Failure and diagnostics

Delegate exceptions fault requests and apply their failure-recycle policy.
An OCE without requested cancellation is a fault for both public API paths.
Replacement creation and any session teardown failure terminate the worker.
If cleanup fails after a delegate failure, preserve the delegate exception for
the request and expose the terminal cleanup exception through `Fault`.

Latch the first terminal fault and close admission before dispatching a single
notification on the thread pool. Contain subscriber exceptions and continue
forwarding pool teardown faults, even if notification arrives after disposal.
Disposal does not rethrow session failures; fault properties remain available.

Telemetry is best effort. A throwing listener must not orphan a dequeued item,
change its outcome, or terminate the worker. Capture the submitting activity's
context per request rather than relying on the worker's inherited context.

### Startup and admission

Start once, enqueue without synchronously waiting for creation, and consume in
FIFO order once a session is available. Account queue depth before publication.
`InitializeAsync` cancellation abandons that caller's wait, not shared startup.
Disposal separately requests cancellation of initial creation.

Snapshot and validate worker/pool options. `QueueCapacity` limits waiting work,
including startup waiters, excluding the active request. Zero is unlimited.
Excess submissions fault immediately with `InvalidOperationException`.
Pool capacity is per worker; scheduling does not retry a rejected submission.
Custom schedulers must select an actual member of a read-only worker list.
Roll back earlier workers if pool construction fails.

### Shutdown

Close admission, apply `Drain` (default) or `CancelPending`, tear down the session
on its owning thread, then complete one shared exit task. Every external async
disposer joins it. Reentrant worker disposal only requests shutdown; a pool
always aggregates real worker exits, not those reentrant fast-path returns.
Queue startup-cancellation callbacks outside worker and pool state locks so
they cannot block the async disposal method before it returns.

Synchronous disposal bounds only the caller's wait. A subsequent async dispose
still joins cleanup. The host's stop token likewise bounds its wait without
abandoning ownership of cleanup.

## Consequences

- Completion can be later than delegate return because required recycle
  teardown is part of the request. A teardown failure can turn a successful
  delegate into a failed request.
- Async callers can queue work before creation finishes and observe creation
  failures through returned requests instead of a synchronous startup wait.
- Delegate execution remains synchronous and thread-affine. Passing async
  delegates or synchronously waiting for nested same-worker work is unsupported.
- Cancellation is cooperative. Pending cancellation is observed on dequeue.
  Running native calls are not forcibly interrupted; hard deadlines require
  process isolation. A canceled/failed initial creation cannot drain queued work.
- STA selects the apartment only; no COM/UI message pump is supplied.
- Worker sessions do not isolate process-global native state.
- The defaults preserve unlimited admission and draining. The new shutdown
  and capacity options are opt-in; option mutations after construction no
  longer affect existing workers.

## Validation

Regression tests cover producer-before-consumer ownership, pending/stale access,
failure parity, lifecycle errors, shared exit, reentrancy, timeout followed by
join, startup FIFO and cancellation, queue depth/capacity, scheduler membership,
fault notification, diagnostics, and hosted deadlines on modern and legacy TFMs.
