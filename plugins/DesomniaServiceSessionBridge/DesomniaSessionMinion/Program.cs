using System;
using System.IO;
using MadWizard.Desomnia.Pipe.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using Autofac.Extensions.DependencyInjection;
using Autofac;
using NLog;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using MadWizard.Desomnia.Pipe;

namespace MadWizard.Desomnia.Minion
{
    static class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                IHost host;
                using (var startup = new MinionStartup(args))
                {
                    MinionConfig config = await startup.ConnectToService(15000);

                    host = CreateHostBuilder(startup.PipeClient, config).Build();
                }

                host.Run();

                Environment.Exit(0); // FIXME ?
            }
            catch (Exception e)
            {
                File.WriteAllText($@"logs/error_minion_{Process.GetCurrentProcess().SessionId}.log", e.ToString());
            }
        }

        public static IHostBuilder CreateHostBuilder(MessagePipeClient pipe, MinionConfig config) =>
            Host.CreateDefaultBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())

                .ConfigureLogging((ctx, builder) =>
                {
                    builder.ClearProviders();
                    builder.AddConsole();
                    builder.AddNLog();
                })

                .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                .ConfigureContainer<ContainerBuilder>((ctx, builder) =>
                {
                    builder.RegisterInstance(config);

                    #region System-Services
                    builder.RegisterType<PipeMessageBroker>()
                        .WithParameter(new TypedParameter(typeof(MessagePipeClient), pipe))
                        //.AttributedPropertiesAutowired()
                        .AsImplementedInterfaces()
                        .SingleInstance()
                        .AsSelf()
                        ;

                    builder.RegisterType<UserInterfaceContext>()
                        .AsImplementedInterfaces()
                        .PropertiesAutowired()
                        .SingleInstance()
                        .AsSelf()
                        ;
                    #endregion

                    #region User-Services
                    builder.RegisterType<LastInputService>()
                        .AsImplementedInterfaces()
                        .SingleInstance()
                        ;
                    builder.RegisterType<SessionControlService>()
                        .AsImplementedInterfaces()
                        .SingleInstance()
                        .AutoActivate()
                        ;
                    builder.RegisterType<NotificationAreaController>()
                        .AsImplementedInterfaces()
                        .SingleInstance()
                        .AutoActivate()
                        ;
                    #endregion
                })
            ;
    }
}