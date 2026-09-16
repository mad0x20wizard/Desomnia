using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Processes.Watch
{
    public class PatternProcessWatch : ProcessWatch
    {
        readonly Regex _pattern;

        /// <summary>Whether the pattern speaks of paths rather than names – settled once, because
        /// every process start asks, and the pattern cannot change under us.</summary>
        public bool IsFilePathPattern { get; }

        public PatternProcessWatch(ProcessWatchInfo info) : base(info.Name)
        {
            _pattern = info.Pattern;

            IsFilePathPattern = _pattern.ToString() is string pattern && (pattern.Contains("\\\\") || pattern.Contains('/'));

            ShouldWatchChildren = info.WatchChildren;

            ((IEventSystem)this)[nameof(Idle)].AddAction(info.OnIdle);
            ((IEventSystem)this)[nameof(Usage)].AddAction(info.OnUsage);

            ((IEventSystem)this)[nameof(Started)].AddAction(info.OnStart);
            ((IEventSystem)this)[nameof(Stopped)].AddAction(info.OnStop);
        }

        public bool ShouldWatchChildren { get; set; }

        protected override bool ShouldWatchProcess(IProcess process)
        {
            if (IsFilePathPattern)
            {
                if (process.ImagePath is string path)
                {
                    if (_pattern.Count(path) > 0)
                        return true;
                }
            }
            else
            {
                if (_pattern.Count(process.Name) > 0)
                    return true;
            }

            if (ShouldWatchChildren && process.Parent is IProcess parent)
            {
                return ShouldWatchProcess(parent);
            }

            return false;
        }
    }
}
