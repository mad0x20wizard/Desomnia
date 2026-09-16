using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;

namespace MadWizard.Desomnia.Service.Controller
{
    [SupportedOSPlatform("windows")]
    public class ObservableServiceController : ServiceController
    {
        private const uint ERROR_SUCCESS = 0;
        private const uint ERROR_SERVICE_DOES_NOT_EXIST = 1060;
        private const uint ERROR_SERVICE_MARKED_FOR_DELETE = 1072;
        private const uint ERROR_SERVICE_NOTIFY_CLIENT_LAGGING = 1294;

        private const uint SC_MANAGER_ENUMERATE_SERVICE = 0x0004;
        private const uint SERVICE_QUERY_STATUS = 0x0004;

        private const uint SERVICE_NOTIFY_STOPPED = 0x00000001;
        private const uint SERVICE_NOTIFY_START_PENDING = 0x00000002;
        private const uint SERVICE_NOTIFY_STOP_PENDING = 0x00000004;
        private const uint SERVICE_NOTIFY_RUNNING = 0x00000008;
        private const uint SERVICE_NOTIFY_CONTINUE_PENDING = 0x00000010;
        private const uint SERVICE_NOTIFY_PAUSE_PENDING = 0x00000020;
        private const uint SERVICE_NOTIFY_PAUSED = 0x00000040;

        private const uint SERVICE_NOTIFY_CREATED = 0x00000080;
        private const uint SERVICE_NOTIFY_DELETED = 0x00000100;
        private const uint SERVICE_NOTIFY_DELETE_PENDING = 0x00000200;

        private const uint SERVICE_NOTIFY_ALL_STATES =
            SERVICE_NOTIFY_STOPPED |
            SERVICE_NOTIFY_START_PENDING |
            SERVICE_NOTIFY_STOP_PENDING |
            SERVICE_NOTIFY_RUNNING |
            SERVICE_NOTIFY_CONTINUE_PENDING |
            SERVICE_NOTIFY_PAUSE_PENDING |
            SERVICE_NOTIFY_PAUSED;

        private const uint SERVICE_NOTIFY_STATUS_CHANGE = 2;

        private const uint WAIT_OBJECT_0 = 0;
        private const uint WAIT_IO_COMPLETION = 0x000000C0;
        private const uint WAIT_FAILED = 0xFFFFFFFF;
        private const uint INFINITE = 0xFFFFFFFF;

        private const int SC_STATUS_PROCESS_INFO = 0;

        private readonly string _observedServiceName;
        private readonly string _observedMachineName;

        private readonly Thread _observerThread;
        private readonly ManualResetEventSlim _startupCompleted = new(false);

        private readonly object _eventLock = new();
        private readonly object _disposeLock = new();

        private readonly ConcurrentQueue<PendingStatusEvent> _pendingEvents = new();

        private EventHandler<ServiceStatusChangedEventArgs>? _statusChanged;

        private ExceptionDispatchInfo? _startupException;

        /*
            * 0 = service does not exist.
            *
            * Windows SERVICE_* states and ServiceControllerStatus both use
            * values 1..7, but conversion is still done explicitly below.
            */
        private int _observedStatus;

        private int _dispatchScheduled;
        private int _disposed;

        private nint _stopEvent;

        /*
            * Everything below here is owned exclusively by _observerThread,
            * except while executing an APC callback on that same thread.
            */
        private nint _scmHandle;
        private nint _serviceHandle;

        private nint _scmNotifyBuffer;
        private nint _serviceNotifyBuffer;

        private GCHandle _selfHandle;

        private bool _scmCallbackFired;
        private uint _scmNotificationStatus;
        private uint _scmNotificationTriggered;

        private bool _serviceCallbackFired;
        private uint _serviceNotificationStatus;
        private uint _serviceNotificationTriggered;
        private uint _serviceNotificationState;


        private delegate void NativeNotificationCallback(nint parameter);

        private static readonly NativeNotificationCallback s_scmCallback =
            ScmNotificationCallback;

        private static readonly NativeNotificationCallback s_serviceCallback =
            ServiceNotificationCallback;

        private static readonly nint s_scmCallbackPtr =
            Marshal.GetFunctionPointerForDelegate(s_scmCallback);

        private static readonly nint s_serviceCallbackPtr =
            Marshal.GetFunctionPointerForDelegate(s_serviceCallback);


        public ObservableServiceController(string serviceName)
            : this(serviceName, ".")
        {
        }

        public ObservableServiceController(
            string serviceName,
            string machineName)
            : base(serviceName, machineName)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ArgumentException(
                    "A service name is required.",
                    nameof(serviceName));

            if (string.IsNullOrWhiteSpace(machineName))
                throw new ArgumentException(
                    "A machine name is required.",
                    nameof(machineName));

            _observedServiceName = serviceName;
            _observedMachineName = machineName;

            _stopEvent = CreateEventW(
                0,
                bManualReset: true,
                bInitialState: false,
                null);

            if (_stopEvent == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            _observerThread = new Thread(ObserverThreadMain)
            {
                IsBackground = true,
                Name = $"Service observer: {serviceName}"
            };

            _observerThread.Start();

            /*
                * Make construction synchronous with the initial SCM query.
                *
                * When the constructor returns, ObservedStatus therefore already
                * represents the initial state of the service.
                */
            _startupCompleted.Wait();

            if (_startupException is not null)
            {
                _observerThread.Join();

                CloseHandle(_stopEvent);
                _stopEvent = 0;

                _startupCompleted.Dispose();

                _startupException.Throw();
            }
        }


        /// <summary>
        /// The service name being observed.
        ///
        /// Unlike ServiceController, the identity of this observer cannot be
        /// changed after construction.
        /// </summary>
        public new string ServiceName
        {
            get => _observedServiceName;

            set => throw new NotSupportedException(
                "The service observed by an ObservableServiceController " +
                "cannot be changed after construction.");
        }


        /// <summary>
        /// The machine being observed.
        /// </summary>
        public new string MachineName
        {
            get => _observedMachineName;

            set => throw new NotSupportedException(
                "The machine observed by an ObservableServiceController " +
                "cannot be changed after construction.");
        }


        /// <summary>
        /// The last service status observed directly from the SCM.
        ///
        /// Null means that the service currently does not exist.
        /// </summary>
        public ServiceControllerStatus? ObservedStatus
        {
            get
            {
                int value = Volatile.Read(ref _observedStatus);

                return value == 0
                    ? null
                    : (ServiceControllerStatus)value;
            }
        }


        /// <summary>
        /// True if the service currently exists.
        /// </summary>
        public bool IsInstalled => ObservedStatus.HasValue;


        /// <summary>
        /// Raised whenever the observed service changes state, appears,
        /// or disappears.
        ///
        /// Status == null means that the service was removed.
        ///
        /// The event is raised on a ThreadPool thread.
        /// </summary>
        public event EventHandler<ServiceStatusChangedEventArgs> StatusChanged
        {
            add
            {
                ArgumentNullException.ThrowIfNull(value);

                lock (_eventLock)
                {
                    ThrowIfDisposed();

                    _statusChanged += value;
                }
            }

            remove
            {
                lock (_eventLock)
                {
                    _statusChanged -= value;
                }
            }
        }


        private void ObserverThreadMain()
        {
            try
            {
                _selfHandle = GCHandle.Alloc(this);

                int notifySize = Marshal.SizeOf<SERVICE_NOTIFY_2>();

                _scmNotifyBuffer =
                    Marshal.AllocHGlobal(notifySize);

                _serviceNotifyBuffer =
                    Marshal.AllocHGlobal(notifySize);

                OpenScm();

                /*
                    * Arm the database notification BEFORE looking for the
                    * service. This closes the race where the service could be
                    * installed/deleted between our initial query and subscribing
                    * to SCM changes.
                    */
                ArmScmNotification();

                ReconcileService(initializing: true);

                _startupCompleted.Set();

                while (true)
                {
                    uint waitResult = WaitForSingleObjectEx(
                        _stopEvent,
                        INFINITE,
                        bAlertable: true);

                    if (waitResult == WAIT_OBJECT_0)
                        break;

                    if (waitResult == WAIT_IO_COMPLETION)
                    {
                        ProcessCompletedNotifications();
                        continue;
                    }

                    if (waitResult == WAIT_FAILED)
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error());
                    }

                    throw new InvalidOperationException(
                        $"Unexpected WaitForSingleObjectEx result: " +
                        $"0x{waitResult:X8}.");
                }
            }
            catch (Exception exception)
            {
                if (!_startupCompleted.IsSet)
                {
                    _startupException =
                        ExceptionDispatchInfo.Capture(exception);
                }
                else
                {
                    /*
                        * A failure here means that monitoring can no longer
                        * reliably continue.
                        *
                        * Installation/uninstallation/reinstallation are NOT
                        * errors and do not reach this path.
                        */
                    Trace.TraceError(
                        $"Service monitoring failed for " +
                        $"'{_observedServiceName}': {exception}");
                }
            }
            finally
            {
                /*
                    * Closing these handles also cancels outstanding
                    * NotifyServiceStatusChange requests.
                    */
                CloseService();
                CloseScm();

                if (_serviceNotifyBuffer != 0)
                {
                    Marshal.FreeHGlobal(_serviceNotifyBuffer);
                    _serviceNotifyBuffer = 0;
                }

                if (_scmNotifyBuffer != 0)
                {
                    Marshal.FreeHGlobal(_scmNotifyBuffer);
                    _scmNotifyBuffer = 0;
                }

                if (_selfHandle.IsAllocated)
                    _selfHandle.Free();

                /*
                    * Also release the constructor if initialization failed
                    * before it reached the normal Set().
                    */
                _startupCompleted.Set();
            }
        }


        private void OpenScm()
        {
            Debug.Assert(_scmHandle == 0);

            string? machineName =
                _observedMachineName == "."
                    ? null
                    : _observedMachineName;

            _scmHandle = OpenSCManagerW(
                machineName,
                null,
                SC_MANAGER_ENUMERATE_SERVICE);

            if (_scmHandle == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }


        private void CloseScm()
        {
            if (_scmHandle == 0)
                return;

            CloseServiceHandle(_scmHandle);
            _scmHandle = 0;
        }


        private void CloseService()
        {
            if (_serviceHandle == 0)
                return;

            CloseServiceHandle(_serviceHandle);
            _serviceHandle = 0;
        }


        /*
            * Watches creation/deletion of services in the entire SCM database.
            *
            * We don't need to parse the list of affected services. Any database
            * change simply causes us to reconcile our one target service.
            */
        private void ArmScmNotification()
        {
            while (true)
            {
                PrepareNotificationBuffer(
                    _scmNotifyBuffer,
                    s_scmCallbackPtr);

                uint error = NotifyServiceStatusChangeW(
                    _scmHandle,
                    SERVICE_NOTIFY_CREATED |
                    SERVICE_NOTIFY_DELETED,
                    _scmNotifyBuffer);

                if (error == ERROR_SUCCESS)
                    return;

                /*
                    * Microsoft explicitly requires opening a new SCM handle
                    * when the notification client has fallen too far behind.
                    */
                if (error == ERROR_SERVICE_NOTIFY_CLIENT_LAGGING)
                {
                    CloseScm();
                    OpenScm();

                    continue;
                }

                throw new Win32Exception(unchecked((int)error));
            }
        }


        /*
            * Returns false if the service became marked-for-delete while the
            * notification was being armed.
            */
        private bool ArmServiceNotification()
        {
            Debug.Assert(_serviceHandle != 0);

            PrepareNotificationBuffer(
                _serviceNotifyBuffer,
                s_serviceCallbackPtr);

            uint error = NotifyServiceStatusChangeW(
                _serviceHandle,
                SERVICE_NOTIFY_ALL_STATES |
                SERVICE_NOTIFY_DELETE_PENDING,
                _serviceNotifyBuffer);

            if (error == ERROR_SUCCESS)
                return true;

            if (error == ERROR_SERVICE_MARKED_FOR_DELETE)
            {
                /*
                    * Critical:
                    *
                    * Do not retain this handle. Its existence would prevent
                    * the SCM from completing deletion.
                    */
                CloseService();

                return false;
            }

            throw new Win32Exception(unchecked((int)error));
        }


        private void PrepareNotificationBuffer(
            nint buffer,
            nint callback)
        {
            var notification = new SERVICE_NOTIFY_2
            {
                dwVersion = SERVICE_NOTIFY_STATUS_CHANGE,

                pfnNotifyCallback = callback,

                pContext = GCHandle.ToIntPtr(_selfHandle)
            };

            Marshal.StructureToPtr(
                notification,
                buffer,
                fDeleteOld: false);
        }


        /*
            * Called after an SCM database notification, or during initial
            * construction, to bring our service attachment in sync with reality.
            */
        private void ReconcileService(bool initializing)
        {
            if (_serviceHandle != 0)
                return;

            nint service = OpenServiceW(
                _scmHandle,
                _observedServiceName,
                SERVICE_QUERY_STATUS);

            if (service == 0)
            {
                uint error = unchecked(
                    (uint)Marshal.GetLastWin32Error());

                if (error == ERROR_SERVICE_DOES_NOT_EXIST)
                {
                    SetObservedStatus(
                        status: null,
                        raiseEvent: !initializing);

                    return;
                }

                if (error == ERROR_SERVICE_MARKED_FOR_DELETE)
                {
                    /*
                        * It still exists in the SCM database but is already on
                        * its way out. Importantly, don't open/retain another
                        * handle that could delay deletion.
                        *
                        * The SCM-level SERVICE_NOTIFY_DELETED event will arrive
                        * when deletion actually completes.
                        */
                    if (initializing)
                    {
                        SetObservedStatus(
                            status: null,
                            raiseEvent: false);
                    }

                    return;
                }

                throw new Win32Exception(unchecked((int)error));
            }

            _serviceHandle = service;

            /*
                * Subscribe first, then query.
                *
                * This avoids losing a state transition occurring between the
                * status query and registration.
                */
            if (!ArmServiceNotification())
                return;

            ServiceControllerStatus status =
                QueryCurrentServiceStatus();

            SetObservedStatus(
                status,
                raiseEvent: !initializing);
        }


        private ServiceControllerStatus QueryCurrentServiceStatus()
        {
            Debug.Assert(_serviceHandle != 0);

            if (!QueryServiceStatusEx(
                    _serviceHandle,
                    SC_STATUS_PROCESS_INFO,
                    out SERVICE_STATUS_PROCESS serviceStatus,
                    (uint)Marshal.SizeOf<SERVICE_STATUS_PROCESS>(),
                    out _))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            return ConvertStatus(serviceStatus.dwCurrentState);
        }


        /*
            * APC callbacks can occur while certain SCM RPC calls are themselves
            * performing alertable waits.
            *
            * Therefore this is deliberately a loop. A new callback may have
            * fired while one of the Process... methods was rearming another
            * notification.
            */
        private void ProcessCompletedNotifications()
        {
            while (_serviceCallbackFired || _scmCallbackFired)
            {
                /*
                    * Handle service deletion first. This releases our service
                    * handle as quickly as possible.
                    */
                if (_serviceCallbackFired)
                {
                    uint notificationStatus =
                        _serviceNotificationStatus;

                    uint notificationTriggered =
                        _serviceNotificationTriggered;

                    uint serviceState =
                        _serviceNotificationState;

                    _serviceCallbackFired = false;

                    ProcessServiceNotification(
                        notificationStatus,
                        notificationTriggered,
                        serviceState);
                }

                if (_scmCallbackFired)
                {
                    uint notificationStatus =
                        _scmNotificationStatus;

                    _scmCallbackFired = false;

                    ProcessScmNotification(
                        notificationStatus);
                }
            }
        }


        private void ProcessServiceNotification(
            uint notificationStatus,
            uint notificationTriggered,
            uint nativeServiceState)
        {
            bool deletePending =
                notificationStatus ==
                    ERROR_SERVICE_MARKED_FOR_DELETE ||
                (notificationStatus == ERROR_SUCCESS &&
                    (notificationTriggered &
                    SERVICE_NOTIFY_DELETE_PENDING) != 0);

            ServiceControllerStatus? newStatus = null;

            if (notificationStatus == ERROR_SUCCESS &&
                TryConvertStatus(
                    nativeServiceState,
                    out ServiceControllerStatus status))
            {
                newStatus = status;
            }

            if (deletePending)
            {
                /*
                    * This is the central difference from the previous
                    * SubscribeServiceChangeNotifications implementation.
                    *
                    * DeleteService() has been called. Drop the handle NOW.
                    *
                    * The SCM can consequently complete deletion and our
                    * SCM-level subscription remains active to observe that.
                    */
                CloseService();
            }
            else if (notificationStatus == ERROR_SUCCESS)
            {
                /*
                    * NotifyServiceStatusChange is one-shot.
                    *
                    * Rearm before dispatching user code.
                    */
                if (_serviceHandle != 0)
                    ArmServiceNotification();
            }
            else
            {
                throw new Win32Exception(
                    unchecked((int)notificationStatus));
            }

            if (newStatus.HasValue)
            {
                SetObservedStatus(
                    newStatus,
                    raiseEvent: true);
            }
        }


        private void ProcessScmNotification(
            uint notificationStatus)
        {
            if (notificationStatus != ERROR_SUCCESS)
            {
                throw new Win32Exception(
                    unchecked((int)notificationStatus));
            }

            /*
                * Rearm FIRST. Creation/deletion may happen again immediately.
                */
            ArmScmNotification();

            ReconcileService(initializing: false);
        }


        /*
            * Native APC callback.
            *
            * Do not invoke ServiceController, SCM APIs, user code, waits, I/O,
            * etc. here. SCM APIs themselves may enter alertable RPC waits.
            */
        private static void ServiceNotificationCallback(
            nint parameter)
        {
            try
            {
                SERVICE_NOTIFY_2 notification =
                    Marshal.PtrToStructure<SERVICE_NOTIFY_2>(
                        parameter);

                GCHandle context =
                    GCHandle.FromIntPtr(notification.pContext);

                if (context.Target
                    is not ObservableServiceController owner)
                {
                    return;
                }

                owner._serviceNotificationStatus =
                    notification.dwNotificationStatus;

                owner._serviceNotificationTriggered =
                    notification.dwNotificationTriggered;

                owner._serviceNotificationState =
                    notification.ServiceStatus.dwCurrentState;

                owner._serviceCallbackFired = true;
            }
            catch
            {
                /*
                    * Never allow an exception to escape across an unmanaged
                    * callback boundary.
                    */
            }
        }


        private static void ScmNotificationCallback(
            nint parameter)
        {
            nint serviceNames = 0;

            try
            {
                SERVICE_NOTIFY_2 notification =
                    Marshal.PtrToStructure<SERVICE_NOTIFY_2>(
                        parameter);

                serviceNames =
                    notification.pszServiceNames;

                GCHandle context =
                    GCHandle.FromIntPtr(notification.pContext);

                if (context.Target
                    is not ObservableServiceController owner)
                {
                    return;
                }

                owner._scmNotificationStatus =
                    notification.dwNotificationStatus;

                owner._scmNotificationTriggered =
                    notification.dwNotificationTriggered;

                owner._scmCallbackFired = true;
            }
            catch
            {
                /*
                    * Never propagate managed exceptions into the APC.
                    */
            }
            finally
            {
                /*
                    * SERVICE_NOTIFY explicitly requires this MULTI_SZ allocation
                    * to be released with LocalFree by the callback recipient.
                    *
                    * We deliberately don't parse it: any SCM database change
                    * causes a cheap reconciliation of our target service.
                    */
                if (serviceNames != 0)
                    LocalFree(serviceNames);
            }
        }


        private void SetObservedStatus(
            ServiceControllerStatus? status,
            bool raiseEvent)
        {
            int newValue =
                status.HasValue
                    ? (int)status.Value
                    : 0;

            int previousValue =
                Interlocked.Exchange(
                    ref _observedStatus,
                    newValue);

            if (previousValue == newValue)
                return;

            /*
                * ServiceController caches status information internally.
                * Invalidate that cache whenever our independent SCM observer
                * notices a change.
                */
            Refresh();

            if (!raiseEvent)
                return;

            ServiceControllerStatus? previousStatus =
                previousValue == 0
                    ? null
                    : (ServiceControllerStatus)previousValue;

            EventHandler<ServiceStatusChangedEventArgs>? handlers;

            lock (_eventLock)
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return;

                handlers = _statusChanged;
            }

            if (handlers is null)
                return;

            /*
                * Capture the subscriber list at the time the change was
                * observed. A subscriber added later should not receive an
                * already-past event.
                */
            _pendingEvents.Enqueue(
                new PendingStatusEvent(
                    handlers,
                    new ServiceStatusChangedEventArgs(
                        previousStatus,
                        status)));

            ScheduleEventDispatcher();
        }


        private void ScheduleEventDispatcher()
        {
            if (Interlocked.CompareExchange(
                    ref _dispatchScheduled,
                    1,
                    0) != 0)
            {
                return;
            }

            ThreadPool.UnsafeQueueUserWorkItem(
                static controller =>
                    controller.DispatchPendingEvents(),
                this,
                preferLocal: false);
        }


        private void DispatchPendingEvents()
        {
            while (true)
            {
                while (_pendingEvents.TryDequeue(
                            out PendingStatusEvent pending))
                {
                    if (Volatile.Read(ref _disposed) != 0)
                        continue;

                    foreach (
                        EventHandler<ServiceStatusChangedEventArgs> handler
                        in pending.Handlers.GetInvocationList())
                    {
                        try
                        {
                            handler(this, pending.Args);
                        }
                        catch (Exception exception)
                        {
                            OnStatusChangedHandlerException(
                                exception);
                        }
                    }
                }

                Interlocked.Exchange(
                    ref _dispatchScheduled,
                    0);

                /*
                    * Close the race between the queue becoming empty and
                    * clearing _dispatchScheduled.
                    */
                if (_pendingEvents.IsEmpty ||
                    Interlocked.CompareExchange(
                        ref _dispatchScheduled,
                        1,
                        0) != 0)
                {
                    return;
                }
            }
        }


        protected virtual void OnStatusChangedHandlerException(
            Exception exception)
        {
            Trace.TraceError(
                $"Unhandled exception in {nameof(StatusChanged)} handler: " +
                exception);
        }


        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(GetType().FullName);
        }


        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                bool firstDispose;

                lock (_disposeLock)
                {
                    firstDispose =
                        Interlocked.Exchange(
                            ref _disposed,
                            1) == 0;

                    if (firstDispose && _stopEvent != 0)
                    {
                        if (!SetEvent(_stopEvent))
                        {
                            Trace.TraceError(
                                $"SetEvent failed while disposing " +
                                $"{nameof(ObservableServiceController)}: " +
                                Marshal.GetLastWin32Error());
                        }
                    }
                }

                /*
                    * Never destroy the stop-event handle until the observer has
                    * returned from WaitForSingleObjectEx.
                    */
                if (_observerThread.IsAlive &&
                    Thread.CurrentThread != _observerThread)
                {
                    _observerThread.Join();
                }

                lock (_disposeLock)
                {
                    if (_stopEvent != 0)
                    {
                        CloseHandle(_stopEvent);
                        _stopEvent = 0;
                    }
                }

                while (_pendingEvents.TryDequeue(out _))
                {
                }

                lock (_eventLock)
                {
                    _statusChanged = null;
                }

                if (firstDispose)
                    _startupCompleted.Dispose();
            }

            base.Dispose(disposing);
        }


        private static ServiceControllerStatus ConvertStatus(
            uint nativeStatus)
        {
            if (TryConvertStatus(
                    nativeStatus,
                    out ServiceControllerStatus status))
            {
                return status;
            }

            throw new InvalidOperationException(
                $"Unknown native service state: {nativeStatus}.");
        }


        private static bool TryConvertStatus(
            uint nativeStatus,
            out ServiceControllerStatus status)
        {
            status = nativeStatus switch
            {
                1 => ServiceControllerStatus.Stopped,
                2 => ServiceControllerStatus.StartPending,
                3 => ServiceControllerStatus.StopPending,
                4 => ServiceControllerStatus.Running,
                5 => ServiceControllerStatus.ContinuePending,
                6 => ServiceControllerStatus.PausePending,
                7 => ServiceControllerStatus.Paused,

                _ => default
            };

            return nativeStatus is >= 1 and <= 7;
        }


        private readonly record struct PendingStatusEvent(
            EventHandler<ServiceStatusChangedEventArgs> Handlers,
            ServiceStatusChangedEventArgs Args);


        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS_PROCESS
        {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
            public uint dwProcessId;
            public uint dwServiceFlags;
        }


        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_NOTIFY_2
        {
            public uint dwVersion;
            public nint pfnNotifyCallback;
            public nint pContext;

            public uint dwNotificationStatus;

            public SERVICE_STATUS_PROCESS ServiceStatus;

            public uint dwNotificationTriggered;

            public nint pszServiceNames;
        }


        [DllImport(
            "advapi32.dll",
            EntryPoint = "OpenSCManagerW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern nint OpenSCManagerW(
            string? machineName,
            string? databaseName,
            uint desiredAccess);


        [DllImport(
            "advapi32.dll",
            EntryPoint = "OpenServiceW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern nint OpenServiceW(
            nint scm,
            string serviceName,
            uint desiredAccess);


        [DllImport(
            "advapi32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryServiceStatusEx(
            nint service,
            int infoLevel,
            out SERVICE_STATUS_PROCESS buffer,
            uint bufferSize,
            out uint bytesNeeded);


        [DllImport(
            "advapi32.dll",
            EntryPoint = "NotifyServiceStatusChangeW")]
        private static extern uint NotifyServiceStatusChangeW(
            nint service,
            uint notifyMask,
            nint notifyBuffer);


        [DllImport("advapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseServiceHandle(
            nint handle);


        [DllImport(
            "kernel32.dll",
            EntryPoint = "CreateEventW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern nint CreateEventW(
            nint eventAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bManualReset,
            [MarshalAs(UnmanagedType.Bool)] bool bInitialState,
            string? name);


        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetEvent(
            nint eventHandle);


        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObjectEx(
            nint handle,
            uint milliseconds,
            [MarshalAs(UnmanagedType.Bool)] bool bAlertable);


        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(
            nint handle);


        [DllImport("kernel32.dll")]
        private static extern nint LocalFree(
            nint memory);
    }


    public sealed class ServiceStatusChangedEventArgs(ServiceControllerStatus? status) : EventArgs
    {
        public ServiceStatusChangedEventArgs(
            ServiceControllerStatus? previous,
            ServiceControllerStatus? status) 
            : this(status)
        {
            PreviousStatus = previous;
        }

        /// <summary>
        /// Null means that the service did not exist.
        /// </summary>
        public ServiceControllerStatus? PreviousStatus { get; }

        /// <summary>
        /// Null means that the service does not exist.
        /// </summary>
        public ServiceControllerStatus? Status { get; } = status;

        public bool IsInstalled => Status.HasValue;
    }
}