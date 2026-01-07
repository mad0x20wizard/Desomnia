using Microsoft.Extensions.Hosting;

namespace MadWizard.Desomnia
{
    public abstract class Module : Autofac.Module
    {
        protected internal virtual void Build(HostApplicationBuilder builder) { }
    }
}