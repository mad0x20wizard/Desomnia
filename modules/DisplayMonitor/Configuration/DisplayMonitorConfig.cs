using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Display.Manager;

namespace MadWizard.Desomnia.Display.Configuration
{
    public class DisplayMonitorConfig
    {
        /// <summary>
        /// Grace period before a disconnect becomes real. Displays emit hot-plug re-negotiation
        /// pulses (removal + immediate re-arrival) on mode changes, TV/AVR power transitions and
        /// link training — observed up to ~20s apart. A watch whose display reappears within this
        /// window continues as if nothing happened.
        /// </summary>
        internal TimeSpan DebounceTime { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Default for every watched display that does not set its own <see cref="DisplayWatchDescriptor.PreventIdle"/>.
        /// Does NOT apply to the built-in panel — see <see cref="DisplayBuiltInDescriptor.PreventIdle"/>.
        /// </summary>
        public PreventIdleType PreventIdle { get; set; } = PreventIdleType.Enabled;

        /// <summary>
        /// Whether watched displays should be held soft-disconnected — the default for every
        /// watched display that does not set its own <see cref="DisplayWatchDescriptor.Disabled"/>.
        /// Does NOT apply to the built-in panel — see <see cref="DisplayBuiltInDescriptor.Disabled"/>.
        /// </summary>
        public bool Disabled { get; set; }

        public DelayedActionInfo? OnIdle { get; set; }
        public DelayedActionInfo? OnUsage { get; set; }

        public IList<DisplayWatchDescriptor> Display { get; set; } = [];

        /// <summary>
        /// The built-in panel — singular on purpose: there is at most one, and it is never
        /// part of the external display selection above.
        /// </summary>
        public DisplayBuiltInDescriptor? DisplayBuiltIn { get; set; }

        public delegate void ConfigureWithDescriptor(DisplayMonitorConfig config, DisplayWatchDescriptor? desc);

        public void Configure(IDisplayExternal display, ConfigureWithDescriptor configure)
        {
            if (Display.Any())
            {
                foreach (var desc in Display)
                    if (desc.Matches(display))
                        configure(this, desc);
            }
            else
            {
                configure(this, null);
            }
        }

        public bool ShouldWatch(IDisplayExternal display)
        {
            // a <DisplayMonitor> that configures no display at all watches every external
            // display; the built-in panel counts as "configured" and never as external
            if (Display.Count == 0)
                return DisplayBuiltIn == null;

            return Display.Any(desc => desc.Matches(display));
        }
    }

    public class DisplayWatchDescriptor
    {
        #region selection criteria (all specified criteria must match)
        /// <summary>Regex on the EDID model name, e.g. "LG HDR 4K"; "*" matches any.</summary>
        public DisplayMatcher? Name { get; set; }

        /// <summary>PnP vendor code, e.g. "GSM" (LG), "DEL" (Dell).</summary>
        public string? Vendor { get; set; }

        public ulong? Product { get; set; }

        /// <summary>Matches the EDID serial string or the numeric serial (decimal).</summary>
        public string? Serial { get; set; }

        /// <summary>Exact native resolution, e.g. "3840x2160".</summary>
        public Resolution? Resolution { get; set; }

        /// <summary>Minimum native pixel dimensions, e.g. minWidth="3840" for "4K or better".</summary>
        public int? MinWidth { get; set; }
        public int? MinHeight { get; set; }
        #endregion

        /// <summary>
        /// Whether this display counts as demand, and when. Overrides the monitor-level
        /// <see cref="DisplayMonitorConfig.PreventIdle"/> default; null inherits it.
        /// </summary>
        public PreventIdleType? PreventIdle { get; set; }

        /// <summary>
        /// Whether this display should be held soft-disconnected while it is watched — a
        /// declared intent (see <see cref="Manager.IDisplay.ShouldBeDisabled"/>) the monitor
        /// asserts for it. Overrides the monitor-level
        /// <see cref="DisplayMonitorConfig.Disabled"/> default; null inherits it.
        /// </summary>
        public bool? Disabled { get; set; }

        public ScheduledActionInfo? OnConnect { get; set; }
        public ScheduledActionInfo? OnDisconnect { get; set; }
        public ScheduledActionInfo? OnPowerOn { get; set; }
        public ScheduledActionInfo? OnPowerOff { get; set; }

        public bool Matches(IDisplayExternal display)
        {
            if (Name != null && !(display.Identity.Name is string name && Name.Match(name)))
                return false;

            if (Vendor != null && !Vendor.Equals(display.Identity.VendorId, StringComparison.OrdinalIgnoreCase))
                return false;

            if (Product is ulong product && display.Identity.ProductCode != product)
                return false;

            if (Serial is string serial)
            {
                bool matchesString = serial.Equals(display.Identity.SerialString, StringComparison.OrdinalIgnoreCase);
                bool matchesNumber = display.Identity.SerialNumber is uint number && serial == number.ToString();

                if (!matchesString && !matchesNumber)
                    return false;
            }

            if (Resolution is Resolution resolution && display.NativeResolution != resolution)
                return false;

            if (MinWidth is int minWidth && (display.NativeResolution?.Width ?? 0) < minWidth)
                return false;

            if (MinHeight is int minHeight && (display.NativeResolution?.Height ?? 0) < minHeight)
                return false;

            return true;
        }
    }

    /// <summary>
    /// The built-in panel — the physical home of the lid, and the only display type that
    /// natively carries the lid events. Deliberately minimal: the panel is always there,
    /// so there are no selection criteria and no connect/disconnect lifecycle.
    /// </summary>
    public class DisplayBuiltInDescriptor
    {
        /// <summary>
        /// Whether the built-in panel counts as demand. Unlike external displays this does
        /// NOT inherit the monitor-level <see cref="DisplayMonitorConfig.PreventIdle"/>:
        /// merely watching the lid must not keep the system awake — demand is strictly opt-in.
        /// </summary>
        public PreventIdleType PreventIdle { get; set; } = PreventIdleType.Never;

        /// <summary>
        /// Whether the built-in panel should be held soft-disconnected. Unlike external
        /// displays this does NOT inherit the monitor-level
        /// <see cref="DisplayMonitorConfig.Disabled"/>: merely watching the lid must not
        /// darken the panel — disabling is strictly opt-in.
        /// </summary>
        public bool Disabled { get; set; }

        /// <summary>Triggered by the panel's lid switch.</summary>
        public ScheduledActionInfo? OnLidOpen { get; set; }
        public ScheduledActionInfo? OnLidClose { get; set; }
    }
}
