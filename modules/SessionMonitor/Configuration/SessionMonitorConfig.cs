using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Process.Configuration;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Session.Configuration
{
    public class SessionConfig<T> where T : SessionDescriptor
    {
        public delegate void ConfigureWithDescriptior(T desc);

        public T? Everyone { get; set; }
        public T? Administrator { get; set; }

        public IList<T> User { get; set; } = [];

        public void Configure<S>(S session, ConfigureWithDescriptior configure) where S : ISession
        {
            if (this.Everyone is T desc)
                if (desc.Name?.Match(session.UserName) ?? true)
                    configure(desc);

            if (session.IsUser)
                foreach (var userDesc in this.User)
                    if (userDesc.Name?.Match(session.UserName) ?? true)
                        configure(userDesc);

            if (session.IsAdministrator)
                if (this.Administrator is T adminDesc)
                    if (adminDesc.Name?.Match(session.UserName) ?? true)
                        configure(adminDesc);
        }
    }

    public class SessionDescriptor
    {
        public SessionMatcher? Name { get; set; }
    }

    public class SessionMonitorConfig : SessionConfig<SessionWatchDescriptor>
    {

    }

    public class SessionWatchDescriptor : SessionDescriptor
    {
        public ScheduledAction? OnIdle { get; set; }
        public ScheduledAction? OnLogin { get; set; }
        public ScheduledAction? OnRemoteLogin { get; set; }
        public ScheduledAction? OnConsolesLogin { get; set; }

        public IList<SessionProcessGroupInfo> Process { get; set; } = [];
    }

    public class SessionProcessGroupInfo : ProcessGroupInfo
    {
        public DelayedAction? OnSessionIdle { get; set; }
    }
}
