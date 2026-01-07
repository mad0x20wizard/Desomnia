namespace MadWizard.Desomnia
{
    public class ConfigFileWatcher : FileSystemWatcher
    {
        readonly CancellationTokenSource _source = new();

        public CancellationToken Token => _source.Token;

        public bool HasChanged => Token.IsCancellationRequested;

        public ConfigFileWatcher(string configPath)
        {
            Path    = System.IO.Path.GetDirectoryName(configPath) ?? string.Empty;
            Filter  = System.IO.Path.GetFileName(configPath);

            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;

            Changed += (sender, e) =>
            {
                EnableRaisingEvents = false; // prevent re-entrancy

                //logger.LogInformation($"Configuration file changed. Restarting...");
                Console.WriteLine($"Configuration file changed. Restarting...");

                _source.Cancel();
            };
        }
    }
}
