using MadWizard.Desomnia.Network.Filter.Rules;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    public abstract class NetworkResource
    {
        protected NetworkResource()
        {
            SubscribeToSelfChanges();
        }

        public event EventHandler? Changed;

        #region FilterRule references
        private HashSet<FilterRule> _filterRules = [];

        internal bool IsReferencedByFilterRules
        {
            get
            {
                lock (_filterRules)
                {
                    return _filterRules.Count > 0;
                }
            }
        }

        internal void AddDynamicFilterRule(PacketFilterRule rule)
        {
            lock(_filterRules)
            {
                if (!_filterRules.Add(rule))
                    return;
            }

            TriggerChanged();
        }

        internal void RemoveDynamicFilterRule(PacketFilterRule rule)
        {
            lock (_filterRules)
            {
                if (!_filterRules.Remove(rule))
                    return;
            }

            TriggerChanged();
        }
        #endregion

        protected void TriggerChanged(EventArgs? args = null) => Changed?.Invoke(this, args ?? EventArgs.Empty);

        protected virtual void SubscribeToSelfChanges()
        {

        }
    }
}
