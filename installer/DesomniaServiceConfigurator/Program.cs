using Autofac;
using CommandLine;
using MadWizard.Desomnia.Service.Installer.Configuration;
using MadWizard.Desomnia.Service.Installer.Options;
using System.Reflection;

// Create your builder.
var builder = new ContainerBuilder();

builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
       .Where(t => t.Name.EndsWith("Configurator"))
       .AsImplementedInterfaces();

switch (Parser.Default.ParseArguments<ConfigInitOptions, ConfigReadOptions>(args).Value)
{
    case ConfigInitOptions opts:
        builder.RegisterType<InitialConfigurationBuilder>()
            .WithParameter(TypedParameter.From(new InitializationPrefs(opts.IniFilePath)))
            .WithParameter(TypedParameter.From(opts.XmlFilePath))
            .AsImplementedInterfaces()
            .SingleInstance()
            .AsSelf();
        break;

    case ConfigReadOptions opts:
        builder.RegisterType<InitialConfigurationReader>()
            .WithParameter(TypedParameter.From(new InitializationPrefs(opts.IniFilePath)))
            .WithParameter(TypedParameter.From(opts.XmlFilePath))
            .AsImplementedInterfaces()
            .SingleInstance()
            .AsSelf();
        break;

    default:
        throw new Exception("Invalid command line arguments.");
}

var container = builder.Build();

return 0;