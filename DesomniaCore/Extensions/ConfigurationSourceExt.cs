using MadWizard.Desomnia.Configuration.Migration;
using Microsoft.Extensions.FileProviders;

namespace Microsoft.Extensions.Configuration
{
    public static class ConfigurationSourceExt
    {
        extension(FileConfigurationSource source)
        {
            public string? FullPath
            {
                get
                {
                    if (source.Path != null)
                    {
                        // the migration layer decorates the physical provider (see
                        // MigratingFileProvider) - the file's real location is underneath
                        var fileProvider = source.FileProvider;

                        if (fileProvider is MigratingFileProvider migrating)
                            fileProvider = migrating.InnerProvider;

                        if (fileProvider is PhysicalFileProvider provider)
                        {
                            return Path.Combine(provider.Root, source.Path);
                        }
                    }

                    return source.Path;
                }
            }
        }
    }
}
