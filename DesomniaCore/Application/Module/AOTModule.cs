using Autofac;

namespace MadWizard.Desomnia
{
    internal class AOTModule : Autofac.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterSource(new AOTMetadataCompatibility());
        }
    }
}
