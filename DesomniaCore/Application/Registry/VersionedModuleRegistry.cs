using MadWizard.Desomnia.Configuration.Migration;

namespace MadWizard.Desomnia.Application.Registry
{
    internal class VersionedModuleRegistry : ModuleRegistry
    {
        /// <summary>
        /// The configuration types of the registered modules, in registration order, discovered
        /// from their <see cref="ConfigurableModule{T}"/> base class — the raw material every
        /// format-specific derivation works from (e.g. the XML collection-element knowledge, see
        /// <c>CollectionElements.Derive</c>). The registry stays free of any configuration format.
        /// </summary>
        internal IEnumerable<Type> ConfigTypes
        {
            get
            {
                static Type? ConfigType(ConfigurableModule module)
                {
                    for (Type? type = module.GetType(); type is not null; type = type.BaseType)
                        if (type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(ConfigurableModule<>))
                            return type.GenericTypeArguments[0];

                    return null;
                }

                lock (this)
                {
                    return [.. _modules.OfType<ConfigurableModule>().Select(ConfigType).OfType<Type>()];
                }
            }
        }

        /// <summary>
        /// The newest format version this build knows. Test seam (simulates a future format);
        /// fixed once the algebra has been computed.
        /// </summary>
        internal uint LatestVersion
        {
            get; set
            {
                if (_locked)
                    throw new InvalidOperationException("The latest version must be set before the registry has been locked.");

                field = value;
            }
        } = ConfigurableModule.LATEST_VERSION;

        /// <summary>The newest format version every registered module accepts (see the algebra).</summary>
        internal uint SupportedVersion { get => field > 0 ? field : throw new InvalidOperationException(); private set; } = 0;
        /// <summary>The oldest format version every registered module reads without migration.</summary>
        internal uint RequiredVersion { get => field > 0 ? field : throw new InvalidOperationException(); private set; } = 0;

        /// <summary>
        /// Checks a document's (post-migration) version against the algebra and throws a
        /// <see cref="ConfigurationMigrationException"/> for a document this build cannot use:
        /// one NEWER than <see cref="SupportedVersion"/> (a newer configuration on an older
        /// build), or one still OLDER than <see cref="RequiredVersion"/> — which, after the
        /// migration had its chance, means migration was disabled or detached. Deliberately
        /// independent of the migration layer (see the class summary).
        /// </summary>
        internal void Validate(uint version)
        {
            if (version > SupportedVersion)
                throw new ConfigurationMigrationException($"The configuration file uses format version {version}, " +
                    $"but this build supports at most version {SupportedVersion}. The file seems to belong to a newer version of the software.");

            if (version < RequiredVersion)
                throw new ConfigurationMigrationException($"The configuration file uses format version {version}, " +
                    $"but this build requires at least version {RequiredVersion}. Update the configuration file manually, or declare " +
                    $"{MigrationSettings.AUTO_MIGRATE_KEY}=\"transient\" or \"persistent\" in the <?config?> header " +
                    "to have it migrated automatically.");
        }

        internal override void Lock()
        {
            lock (this)
            {
                (SupportedVersion, RequiredVersion, _) = ComputeVersions();

                base.Lock();
            }
        }

        private (uint Supported, uint Required, ConfigurableModule? MostDemanding) ComputeVersions()
        {
            uint latest = LatestVersion;

            uint supported = latest;
            uint required = 1;

            ConfigurableModule? mostDemanding = null;
            ConfigurableModule? mostRestrictive = null;

            foreach (var module in _modules.OfType<ConfigurableModule>())
            {
                uint max = module.MaxVersion;
                uint min = module.MinVersion;

                if (max < supported)
                    (supported, mostRestrictive) = (max, module);

                if (min > required)
                    (required, mostDemanding) = (min, module);
            }

            if (required > supported)
            {
                // name the culprits - either side may be the product itself (no module raised
                // the requirement above the baseline, or none limits the format below latest)
                string demanding = mostDemanding is not null ? $"{mostDemanding.GetType().Name} requires version {required}" : $"the format requires at least version {required}";
                string restrictive = mostRestrictive is not null ? $"{mostRestrictive.GetType().Name} supports at most version {supported}" : $"this build supports at most version {supported}";

                throw new ConfigurationMigrationException($"The configuration format cannot be satisfied: {demanding}, but {restrictive}.");
            }

            return (supported, required, mostDemanding);
        }
    }
}
