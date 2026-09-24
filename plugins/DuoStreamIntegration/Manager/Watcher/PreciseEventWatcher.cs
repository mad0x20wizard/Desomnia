using System.Diagnostics.Eventing.Reader;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    /**
     * Since Version 1.6.1 Duo writes the name of the instance unambiguously to the EventLog.
     */
    internal class PreciseEventWatcher : EventWatcher
    {
        internal new static readonly Version MinVersion = new(1, 6, 1);

        protected override EventLogQuery CreateQuery()
        {
            var eventPaths = string.Join(" or ", Enum.GetValues<DuoEventID>().Select(id => "EventID=" + (int)id));

            var xpath = $"*[System[Provider[@Name='Duo'] and ({eventPaths})]]";

            return new EventLogQuery("Duo", PathType.LogName, xpath); // Events are now written to dedicated event log "Duo"
        }

        protected override DuoInstance FindInstance(IEnumerable<DuoInstance> instances, EventRecord record)
        {
            var matches =
                from item in record.Properties let text = item.Value.ToString()
                    where text is not null
                from instance in instances 
                    where text.Contains('"' + instance.Name + '"') 
                select instance;

            try
            {
                return matches.SingleOrDefault() ?? throw new KeyNotFoundException("Duo event does not identify a known instance.");
            }
            catch (InvalidOperationException)
            {
                throw new AmbiguousInstanceNameException([.. matches.Select(instance => instance.Name)]);
            }
        }
    }
}