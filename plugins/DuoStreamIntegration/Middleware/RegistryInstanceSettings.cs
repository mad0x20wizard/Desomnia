using Autofac;
using Autofac.Core.Resolving.Pipeline;
using Microsoft.Win32;
using static MadWizard.Desomnia.Service.Duo.Manager.DuoInstance;

namespace MadWizard.Desomnia.Network.Middleware
{
    public sealed class RegistryInstanceSettings : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.ParameterSelection;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            if (context.FirstParameterOfType<RegistryKey>() is RegistryKey key)
            {
                context.ChangeParameters([ ..context.Parameters, TypedParameter.From(ReadSettings(key)) ]); 
                
                next(context);
            }
        }

        private static InstanceSettings ReadSettings(RegistryKey key)
        {
            try
            {
                return new InstanceSettings
                {
                    Name = key.Name.Split('\\').Last(),
                    Port = key["Port"] is int port ? (ushort)port : throw new ArgumentNullException("Port"),
                    UserName = key["UserName"] is string name ? name : throw new ArgumentNullException("UserName"),
                    IsSandboxed = key["Sandboxed"] is int sandboxed && sandboxed == 1
                };
            }
            catch
            {
                key.Dispose();
                throw;
            }
        }
    }
}
