using HarmonyLib;
using MadWizard.Desomnia.Service;
using MadWizard.Desomnia.Service.Controller;
using MadWizard.Desomnia.Service.Duo;
using Microsoft.Win32;
using System.Diagnostics.Eventing.Reader;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.ServiceProcess;

namespace DuoStreamIntegration.Tests;

// Patches live only in the test process and only intercept objects registered below.
// No monitor, command, event-processing, or lifetime method is replaced.
internal static class WindowsMocks
{
    internal static readonly ConditionalWeakTable<DuoService, FakeDuoService> Services = new();
    internal static readonly ConditionalWeakTable<EventLogWatcher, FakeEventSource> EventSources = new();
    internal static readonly ConditionalWeakTable<EventLogRecord, RecordData> Records = new();
    private static readonly Lazy<bool> Installed = new(Install);

    // Install before test methods are JIT-compiled, including Release/inlined callers.
    [ModuleInitializer]
    internal static void Initialize() => _ = Installed.Value;

    private static bool Install()
    {
        var harmony = new Harmony("Desomnia.Duo.Tests.WindowsMocks");
        void Patch(MethodBase method, string prefix) =>
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(WindowsMocks), prefix));

        Patch(AccessTools.PropertyGetter(typeof(DuoService), nameof(DuoService.Settings)), nameof(GetSettings));
        Patch(AccessTools.Method(typeof(WindowsServiceExt), "get_PID", [typeof(ServiceController)]), nameof(GetPid));
        Patch(AccessTools.Method(typeof(WindowsServiceExt), "get_ExecutablePath", [typeof(ServiceController)]), nameof(GetPath));
        Patch(AccessTools.Method(typeof(WindowsServiceExt), "get_Version", [typeof(ServiceController)]), nameof(GetVersion));
        Patch(AccessTools.PropertySetter(typeof(EventLogWatcher), nameof(EventLogWatcher.Enabled)), nameof(SetEnabled));
        Patch(AccessTools.Method(typeof(EventLogWatcher), "Dispose", [typeof(bool)]), nameof(DisposeSource));
        Patch(AccessTools.PropertyGetter(typeof(EventLogRecord), nameof(EventLogRecord.Id)), nameof(GetRecordId));
        Patch(AccessTools.PropertyGetter(typeof(EventLogRecord), nameof(EventLogRecord.Properties)), nameof(GetRecordProperties));
        Patch(AccessTools.Method(typeof(EventLogRecord), "Dispose", [typeof(bool)]), nameof(DisposeRecord));
        Patch(AccessTools.Method(typeof(RegistryKey), nameof(RegistryKey.OpenSubKey), [typeof(string), typeof(bool)]), nameof(OpenDuoKey));
        return true;
    }

    private static bool OpenDuoKey(RegistryKey __instance, string __0, bool __1, ref RegistryKey? __result)
    {
        if (TestDuoRegistry.Current is not { } registry || __instance.Name != Registry.LocalMachine.Name)
            return true;

        if (__0.Equals(DuoService.REG_Duo, StringComparison.OrdinalIgnoreCase))
            __result = registry.Key.OpenSubKey(string.Empty, __1);
        else if (__0.StartsWith(DuoService.REG_Duo + "\\", StringComparison.OrdinalIgnoreCase))
            __result = registry.Key.OpenSubKey(__0[(DuoService.REG_Duo.Length + 1)..], __1);
        else
            return true;

        return false;
    }

    private static bool GetSettings(DuoService __instance, ref DuoSettings __result)
    {
        if (!Services.TryGetValue(__instance, out var fake)) return true;
        __result = fake.Settings;
        return false;
    }

    private static bool GetPid(ServiceController __0, ref uint? __result)
    {
        if (__0 is not DuoService service || !Services.TryGetValue(service, out var fake)) return true;
        __result = fake.PID;
        return false;
    }

    private static bool GetPath(ServiceController __0, ref string __result)
    {
        if (__0 is not DuoService service || !Services.TryGetValue(service, out var fake)) return true;
        __result = fake.ExecutablePath;
        return false;
    }

    private static bool GetVersion(ServiceController __0, ref Version __result)
    {
        if (TestServiceVersion.Current is { } version && __0.GetType() == typeof(ServiceController))
        {
            __result = version.Version ?? throw new InvalidOperationException("This watch mode must not query the service version.");
            return false;
        }

        if (__0 is not DuoService service || !Services.TryGetValue(service, out var fake)) return true;
        __result = fake.Version;
        return false;
    }

    private static bool SetEnabled(EventLogWatcher __instance, bool __0)
    {
        if (!EventSources.TryGetValue(__instance, out var fake)) return true;
        AccessTools.Field(typeof(EventLogWatcher), "_isSubscribing").SetValue(__instance, __0);
        fake.SetEnabled(__0);
        return false;
    }

    private static bool DisposeSource(EventLogWatcher __instance)
    {
        if (!EventSources.TryGetValue(__instance, out var fake)) return true;
        fake.SetEnabled(false);
        return false;
    }

    private static bool GetRecordId(EventLogRecord __instance, ref int __result)
    {
        if (!Records.TryGetValue(__instance, out var data)) return true;
        __result = data.Id;
        return false;
    }

    private static bool GetRecordProperties(EventLogRecord __instance, ref IList<EventProperty> __result)
    {
        if (!Records.TryGetValue(__instance, out var data)) return true;
        __result = data.Properties;
        return false;
    }

    private static bool DisposeRecord(EventLogRecord __instance)
    {
        if (!Records.TryGetValue(__instance, out var data)) return true;
        data.Disposed = true;
        return false;
    }

    internal sealed class RecordData(int id, string[] properties)
    {
        public int Id { get; } = id;
        public IList<EventProperty> Properties { get; } = properties.Select(value =>
            (EventProperty)AccessTools.Constructor(typeof(EventProperty), [typeof(object)]).Invoke([value])).ToList();
        public bool Disposed { get; set; }
    }
}

internal sealed class FakeDuoService
{
    private static readonly FieldInfo Status = AccessTools.Field(typeof(ObservableServiceController), "_observedStatus");
    private static readonly FieldInfo Handlers = AccessTools.Field(typeof(ObservableServiceController), "_statusChanged");
    public DuoService Service { get; }

    public FakeDuoService()
    {
        WindowsMocks.Initialize();
        // Skip the constructor that starts the SCM observer. Keep the real managed
        // status getter and subscription accessors, including their synchronization.
        Service = (DuoService)RuntimeHelpers.GetUninitializedObject(typeof(DuoService));
        GC.SuppressFinalize(Service);
        AccessTools.Field(typeof(ObservableServiceController), "_eventLock").SetValue(Service, new object());
        WindowsMocks.Services.Add(Service, this);
        ObservedStatus = ServiceControllerStatus.Running;
    }

    public ServiceControllerStatus? ObservedStatus
    {
        get => Service.ObservedStatus;
        set => Status.SetValue(Service, value is { } status ? (int)status : 0);
    }
    public uint? PID => ObservedStatus == ServiceControllerStatus.Running ? 123u : null;
    public string ExecutablePath => "test-duo.exe";
    public Version Version => new(1, 5, 7);
    public DuoSettings Settings { get; set; } = new() { Port = 38299, Instances = [DuoTestSupport.Settings()] };

    private EventHandler<ServiceStatusChangedEventArgs>? Snapshot() =>
        (EventHandler<ServiceStatusChangedEventArgs>?)Handlers.GetValue(Service);

    public void Publish(ServiceControllerStatus status)
    {
        var previous = ObservedStatus;
        ObservedStatus = status;
        Snapshot()?.Invoke(Service, new(previous, status));
    }

    public Action CaptureRunningNotification()
    {
        var handlers = Snapshot();
        return () => handlers?.Invoke(Service, new(ServiceControllerStatus.Running));
    }
}
