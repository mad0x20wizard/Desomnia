using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Configuration.Model;
using MadWizard.Desomnia.Configuration.Xml;
using Microsoft.Extensions.FileProviders;
using NLog;

namespace Microsoft.Extensions.Configuration.Xml
{
    /*
     * The self-contained XML file source: reads the file through the XmlConfigurationReader
     * into the abstract ConfigNode form and flattens it into provider data - the same key
     * layout the stock provider produces, plus what the old pipeline achieved by rewriting
     * the XML before the stock parse (presence values for bare elements, synthesized names
     * for nameless collection items), plus document order all the way into GetChildren().
     *
     * It stands for itself: no injection points. In the augmenting mode the EnvironmentMonitor
     * CONSUMES the same reader output and exposes its own configuration source to the host;
     * this source reaches the host directly only in passthrough mode. File watching is the
     * stock FileConfigurationSource machinery (ReloadOnChange via IFileProvider.Watch).
     */
    public class ExtendedXmlConfigurationSource : XmlConfigurationSource, IPersistentConfigurationSource
    {
        /// <summary>Which element names form collections of complex items - derived from the
        /// modules' configuration types, shared with the environment merger.</summary>
        public CollectionElementRegistry Collections { get; } = new();

        public ExtendedXmlConfigurationSource(string path, bool optional = false, bool reloadOnChange = false)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException($"path = {path}");

            Path = path;
            Optional = optional;
            ReloadOnChange = reloadOnChange;

            ResolveFileProvider();
        }

        /// <summary>
        /// Registers an explicit name builder for nameless elements of the given collection.
        /// Use this when code relies on the synthesized name format (which is otherwise an
        /// implementation detail, defaulting to "{elementName}#{nr}").
        /// </summary>
        public ExtendedXmlConfigurationSource AddCollectionNameBuilder(string elementName, CollectionNameBuilder builder)
        {
            Collections.AddCollectionNameBuilder(elementName, builder);

            return this;
        }

        /// <summary>
        /// Walks the given configuration type and records the names of all properties holding
        /// collections of complex items. XML elements with these names are collection elements
        /// and get a synthesized name attribute if they don't carry one.
        /// </summary>
        public ExtendedXmlConfigurationSource AddCollectionElementsOf(Type configType)
        {
            Collections.AddCollectionElementsOf(configType);

            return this;
        }

        /// <summary>
        /// Reads the <c>&lt;?global key="value"?&gt;</c> processing instructions outside the
        /// root element — the persistent (process-lifetime) configuration, read before any
        /// container is built. A missing file yields no entries (the non-optional provider
        /// reports the missing file when the application configuration is built).
        /// </summary>
        public IEnumerable<KeyValuePair<string, string>> LoadPersistentConfiguration()
        {
            using var stream = TryOpenFile();

            if (stream is null)
                return [];

            return XmlConfigurationReader.Read(stream).GlobalDirectives
                .Select(directive => new KeyValuePair<string, string>(directive.Key, directive.Value))
                .ToList();
        }

        private Stream? TryOpenFile()
        {
            if (FileProvider?.GetFileInfo(Path!) is { Exists: true, IsDirectory: false } file)
                return file.CreateReadStream();

            // no file provider resolved yet (a relative path before EnsureDefaults)
            if (FileProvider is null && Path is string path && File.Exists(path))
                return File.OpenRead(path);

            return null;
        }

        public override IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            EnsureDefaults(builder);

            return new CustomXmlConfigurationProvider(this);
        }
    }

    class CustomXmlConfigurationProvider(ExtendedXmlConfigurationSource source) : XmlConfigurationProvider(source)
    {
        static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        OrderedConfigurationData _data = OrderedConfigurationData.Empty;

        bool _loaded;

        public override void Load(Stream stream)
        {
            try
            {
                var file = XmlConfigurationReader.Read(stream);

                _data = new OrderedConfigurationData(
                    ConfigNodeFlattener.Flatten(file.ToConfigNode(), source.Collections));
            }
            catch (Exception ex) when (_loaded)
            {
                // a bad edit on the reload path: keep serving the last good data - the
                // configuration pipeline is the authority that reacts to the bad edit (it
                // exits the application); the stock behavior of clearing the data would feed
                // the options system an empty configuration in the meantime
                Logger.Warn(ex, $"Failed to reload '{source.Path}'; keeping the current configuration data.");
                return;
            }

            Data = _data.Data;

            _loaded = true;
        }

        /// <summary>
        /// Values always come from the last successfully loaded data — NOT from the base
        /// class's <c>Data</c>: the stock reload path clears <c>Data</c> when the watched
        /// file is momentarily missing (an editor's delete+rename save) without ever calling
        /// <see cref="Load(Stream)"/>, which would serve an empty configuration while
        /// <see cref="GetChildKeys"/> still enumerates the old sections. Reading through
        /// _data keeps values and child keys consistent and last-good.
        /// </summary>
        public override bool TryGet(string key, out string? value)
            => _data.Data.TryGetValue(key, out value);

        /// <summary>Child keys in document order (the stock provider would sort them),
        /// so collection binding sees items exactly as they were written.</summary>
        public override IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
            => _data.GetChildKeys(earlierKeys, parentPath);
    }

    public static class FileConfigurationSourceExt
    {
        extension (FileConfigurationSource source)
        {
            public string? FullPath
            {
                get
                {
                    if (source.Path != null)
                    if (source.FileProvider is PhysicalFileProvider provider)
                    {
                        return Path.Combine(provider.Root, source.Path);
                    }

                    return source.Path;
                }
            }
        }
    }
}
