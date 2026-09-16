# DuoStreamIntegration tests

Run the complete suite from the repository root:

```powershell
dotnet test tests/DuoStreamIntegration.Tests/DuoStreamIntegration.Tests.csproj
```

The tests use the current service context, manager/watcher interfaces, and instance model. The old generation, process-subscription, and lease tests have been replaced by tests for their current responsibilities.

Concurrency tests use controlled query completion and notification delivery. Service and listener tests do not require Duo, registry entries, sockets, or firewall changes.

`Instrumentation/` contains test-only Harmony patches for concrete `DuoService` objects and Windows event-log objects. Patches intercept only registered test objects; other objects retain their normal behavior. The service fixture skips the constructor that starts the Windows service observer, supplies service metadata, and uses the real managed status getter and event subscription accessors. No `IDuoService` interface or production test hooks are needed.

`TestEventWatcher` delivers actual `EventRecordWrittenEventArgs` to the production callback and runs the production watch loop. Native subscription and record access are intercepted. An observed channel acknowledges processing when the consumer requests its next item, allowing tests to coordinate without sleeps. These fixtures intentionally depend on private fields and Windows/.NET implementation details; structural changes may require updating the instrumentation. The Harmony dependency is confined to this test project.

Regression tests have an `Issue` trait matching the review:

- `2`: disposal of delivered event records, including ignored and unmatched events.
- `3`: sequential event processing, watcher shutdown, request-timeout recovery, and actions that issue commands.
- `5`: retry after an initial API failure, cancellation during retry, and disposal with a pending retry.
- `6`: adapters attach during activation before the build callback starts monitoring, whether Duo is already running or starts later.

All regressions are active, not skipped. The full suite reports failures for behavior that has not yet been fixed. To run just the event concurrency regressions, use `--filter "Issue=3"`; to run the suite excluding issues 5 and 6, use `--filter "Issue!=5&Issue!=6"`.

The startup contract is deliberately modest: load initial state, then watch subsequent changes. A manual transition during the query/subscription handover can be missed; tests do not require an atomic snapshot/event handover. Tests cover initially running and stopped instances, subsequent manual changes in both directions, and duplicate notifications. Event mode has no periodic reconciliation guarantee; polling mode refreshes periodically.

Readers that can overlap service transitions use `TakeSnapshot()`. The enumeration regression tests this explicit snapshot contract; ordinary monitor enumeration does not promise a snapshot.

Native Windows event-log subscription is not exercised by these tests; it still needs a live Duo smoke test.
