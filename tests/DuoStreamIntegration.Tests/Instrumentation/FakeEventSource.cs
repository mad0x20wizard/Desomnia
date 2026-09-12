using HarmonyLib;
using MadWizard.Desomnia.Service.Duo;
using MadWizard.Desomnia.Service.Duo.Manager.Watcher;
using System.Diagnostics.Eventing.Reader;
using System.Reflection;
using System.Runtime.CompilerServices;
using static DuoStreamIntegration.Tests.DuoTestSupport;

namespace DuoStreamIntegration.Tests;

// Numeric Windows event IDs belong to the transport fixture; production visibility is unchanged.
internal enum DuoEventID { ServiceStarted = 1000, InstanceStarted = 1003, InstanceStopped = 1004, InstanceError = 1005, Resuming = 1010 }

internal sealed class TestEventWatcher : EventWatcher
{
    private readonly FakeEventSource _source;
    public TestEventWatcher()
    {
        WindowsMocks.Initialize();
        _source = new FakeEventSource(this);
    }

    public TaskCompletionSource Subscribed => _source.Subscribed;
    public TaskCompletionSource Unsubscribed => _source.Unsubscribed;
    public IReadOnlyList<WindowsMocks.RecordData> DeliveredRecords => _source.DeliveredRecords;
    public Task SendAsync(DuoEventID id, params string[] properties) => _source.SendAsync(id, properties);
    public bool Publish(DuoEventID id, params string[] properties) => _source.Publish(id, properties);
    public void PublishError(Exception error) => _source.PublishError(error);
}

internal sealed class FakeEventSource
{
    private readonly EventLogWatcher _native;
    private readonly IObservedChannel _channel;
    private readonly Type _signalType;
    private readonly DuoInstance _barrier = Instance("TestBarrier");
    private readonly Queue<(DuoEventID Id, string[] Properties, TaskCompletionSource Done)> _pending = new();
    private bool _enabled;
    private readonly List<WindowsMocks.RecordData> _records = [];
    public TaskCompletionSource Subscribed { get; } = Signal();
    public TaskCompletionSource Unsubscribed { get; } = Signal();
    public IReadOnlyList<WindowsMocks.RecordData> DeliveredRecords => _records;

    public FakeEventSource(EventWatcher watcher)
    {
        _native = (EventLogWatcher)AccessTools.Field(typeof(EventWatcher), "<Watcher>k__BackingField").GetValue(watcher)!;
        var channelField = AccessTools.Field(typeof(EventWatcher), "_channel");
        _signalType = channelField.FieldType.GetGenericArguments()[0];
        _channel = (IObservedChannel)Activator.CreateInstance(typeof(ObservedChannel<>).MakeGenericType(_signalType))!;
        channelField.SetValue(watcher, _channel);
        WindowsMocks.EventSources.Add(_native, this);
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (enabled)
        {
            Subscribed.TrySetResult();
            while (_pending.TryDequeue(out var item))
                _ = CompleteAsync(Deliver(item.Id, item.Properties), item.Done);
        }
        else
        {
            _channel.Finish();
            _barrier.Dispose();
            Unsubscribed.TrySetResult();
        }
    }

    public Task SendAsync(DuoEventID id, string[] properties)
    {
        if (_enabled) return Deliver(id, properties);
        if (Unsubscribed.Task.IsCompleted) throw new InvalidOperationException("Watcher has stopped.");
        var done = Signal();
        _pending.Enqueue((id, properties, done));
        return done.Task;
    }

    public bool Publish(DuoEventID id, string[] properties)
    {
        if (!_enabled) return false;
        _ = Deliver(id, properties);
        return true;
    }

    private EventHandler<EventRecordWrittenEventArgs>? Handlers =>
        (EventHandler<EventRecordWrittenEventArgs>?)AccessTools.Field(typeof(EventLogWatcher), "EventRecordWritten").GetValue(_native);

    private Task Deliver(DuoEventID id, string[] properties)
    {
        var record = (EventLogRecord)RuntimeHelpers.GetUninitializedObject(typeof(EventLogRecord));
        GC.SuppressFinalize(record);
        var data = new WindowsMocks.RecordData((int)id, properties);
        WindowsMocks.Records.Add(record, data);
        _records.Add(data);
        var args = (EventRecordWrittenEventArgs)AccessTools.Constructor(typeof(EventRecordWrittenEventArgs), [typeof(EventLogRecord)]).Invoke([record]);
        var writes = _channel.Writes;
        Handlers?.Invoke(_native, args);
        if (_channel.Writes != writes) return _channel.LastWrite;

        // Ignored/malformed records enqueue no work. Use a no-op state signal as a
        // barrier so tests can wait for prior work without repairing instance state.
        var barrier = Activator.CreateInstance(_signalType, nonPublic: true)!;
        AccessTools.Property(_signalType, "Instance").SetValue(barrier, _barrier);
        return _channel.Write(barrier);
    }

    public void PublishError(Exception error)
    {
        var args = (EventRecordWrittenEventArgs)AccessTools.Constructor(typeof(EventRecordWrittenEventArgs), [typeof(Exception)]).Invoke([error]);
        Handlers?.Invoke(_native, args);
    }

    private static async Task CompleteAsync(Task processing, TaskCompletionSource done)
    {
        try { await processing; done.TrySetResult(); }
        catch (Exception ex) { done.TrySetException(ex); }
    }
}
