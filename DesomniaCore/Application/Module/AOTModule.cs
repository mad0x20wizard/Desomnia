using Autofac;

namespace MadWizard.Desomnia
{
    internal sealed class AOTModule : Autofac.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterSource(new AOTMetadataCompatibility());
        }
    }
}
