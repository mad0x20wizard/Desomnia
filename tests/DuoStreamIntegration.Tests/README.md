# DuoStreamIntegration tests

Run the complete suite from the repository root on Windows:

```powershell
dotnet test tests/DuoStreamIntegration.Tests/DuoStreamIntegration.Tests.csproj
```

The tests follow the current ownership model:

- Watchers produce async streams of `WatchSignal`; the composite merges their signals.
- `DuoServiceContext` queries and updates the backend running state and completes commands only when both running state and session presence match.
- `SessionWatchAdapter` configures and attaches session watches.
- `RegistryWatcher` binds registry session IDs, clears stale startup values, and removes retained IDs on logoff.

Context tests cover logout gaps, session restarts without a backend transition, command serialization, timeouts, query recovery, and actions that issue commands. Watcher tests cover signal contents, ordering, cancellation, disposal, and failure isolation. Registration tests verify all compatible watchers are included in a composite scoped to each service context.

Concurrency tests use controlled query completion and notification delivery. The controlled watcher acknowledges a signal after the real context finishes processing it, avoiding timing sleeps. Event-log tests deliver actual `EventRecordWrittenEventArgs` to the production callback and inspect the resulting signal stream.

`Instrumentation/` contains test-only Harmony patches for registered concrete service and Windows event-log objects. Service tests do not start Duo or the Windows service observer. Listener tests do not open sockets or change firewall settings. Native event-log subscription and record access are intercepted; a live Duo smoke test is still needed for those Windows interactions.

Registry tests exercise real Windows notifications using temporary keys under `HKCU\Software\Desomnia.Tests\<unique-id>`. Test-only patches redirect Duo registry reads to these keys, which are removed on disposal. These tests require permission to create and delete the temporary keys, but do not modify the installed Duo configuration.

Initial backend state is loaded before watching subsequent changes. Tests do not require an atomic snapshot/subscription handover. Registry updates assume the session is already known when Duo publishes its ID. Event-only monitoring has no periodic reconciliation guarantee; polling provides periodic refreshes.

The fixtures depend on private Windows/.NET fields for native transport interception. Structural changes may require updating that instrumentation; no production test hooks are used.
