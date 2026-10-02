using HarmonyLib;
using MadWizard.Desomnia.Service.Duo.Manager.Watcher;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.CompilerServices;
using static DuoStreamIntegration.Tests.DuoTestSupport;

namespace DuoStreamIntegration.Tests;

// Only the Windows event transport is replaced; EventWatcher's real callback runs.
internal enum DuoEventID { ServiceStarted = 1000, InstanceStarted = 1003, InstanceStopped = 1004, InstanceError = 1005, Resuming = 1010 }

internal sealed class TestEventWatcher : EventWatcher
{
    private readonly FakeEventSource _source;
    public TestEventWatcher() => _source = new FakeEventSource(this);
    public TaskCompletionSource Subscribed => _source.Subscribed;
    public TaskCompletionSource Unsubscribed => _source.Unsubscribed;
    public IReadOnlyList<WindowsMocks.RecordData> DeliveredRecords => _source.DeliveredRecords;
    public bool Publish(Tests.DuoEventID id, params string[] properties) => _source.Publish(id, properties);
    public void PublishError(Exception error) => _source.PublishError(error);
}

internal sealed class FakeEventSource
{
    private readonly EventLogWatcher _native;
    private bool _enabled;
    private readonly List<WindowsMocks.RecordData> _records = [];
    public TaskCompletionSource Subscribed { get; } = Signal();
    public TaskCompletionSource Unsubscribed { get; } = Signal();
    public IReadOnlyList<WindowsMocks.RecordData> DeliveredRecords => _records;

    public FakeEventSource(EventWatcher watcher)
    {
        _native = (EventLogWatcher)AccessTools.Field(typeof(EventWatcher), "_watcher").GetValue(watcher)!;
        WindowsMocks.EventSources.Add(_native, this);
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (enabled) Subscribed.TrySetResult();
        else Unsubscribed.TrySetResult();
    }

    private EventHandler<EventRecordWrittenEventArgs>? Handlers =>
        (EventHandler<EventRecordWrittenEventArgs>?)AccessTools.Field(typeof(EventLogWatcher), "EventRecordWritten").GetValue(_native);

    public bool Publish(DuoEventID id, string[] properties)
    {
        if (!_enabled) return false;
        var record = (EventLogRecord)RuntimeHelpers.GetUninitializedObject(typeof(EventLogRecord));
        GC.SuppressFinalize(record);
        var data = new WindowsMocks.RecordData((int)id, properties);
        WindowsMocks.Records.Add(record, data);
        _records.Add(data);
        var args = (EventRecordWrittenEventArgs)AccessTools.Constructor(typeof(EventRecordWrittenEventArgs), [typeof(EventLogRecord)]).Invoke([record]);
        Handlers?.Invoke(_native, args);
        return true;
    }

    public void PublishError(Exception error)
    {
        var args = (EventRecordWrittenEventArgs)AccessTools.Constructor(typeof(EventRecordWrittenEventArgs), [typeof(Exception)]).Invoke([error]);
        Handlers?.Invoke(_native, args);
    }
}
