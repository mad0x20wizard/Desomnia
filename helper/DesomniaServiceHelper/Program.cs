using CommandLine;
using MadWizard.Desomnia.Service.Helper;
using MadWizard.Desomnia.Service.Helper.Options;
using System.ServiceProcess;

ServiceController service = new("DesomniaService");

switch (Parser.Default.ParseArguments<ReadOptions, ServiceOptions>(args).Value)
{
    case ServiceOptions opts when opts.Command == "start":
        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, opts.Timeout);
        return 0;
    case ServiceOptions opts when opts.Command == "stop":
        service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, opts.Timeout);
        return 0;

    case ReadOptions opts when opts.Value == "LastInputTime":
        long ticks = User.LastInputTimeTicks;
        Console.Write($"{ticks}");
        return 0;
}

return 1;