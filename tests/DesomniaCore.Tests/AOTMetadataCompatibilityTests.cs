using Autofac;
using Autofac.Features.Metadata;
using Xunit;

namespace MadWizard.Desomnia.Tests;

public class AOTMetadataCompatibilityTests
{
    public sealed class Plugin { }

    public sealed class Metadata
    {
        public Metadata() { }

        // Autofac prefers this constructor, while our AOT view builder uses the parameterless
        // constructor and property setters. Fail even under JIT if Autofac bypasses our source.
        public Metadata(IDictionary<string, object> metadata)
            => throw new InvalidOperationException("Autofac's metadata view provider was used.");

        public int? Network { get; set; }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MetadataCollectionsUseAotViewsRegardlessOfSourceOrderAndScope(
        bool aotRegisteredLast, bool resolveRootFirst)
    {
        var builder = new ContainerBuilder();
        if (!aotRegisteredLast) builder.RegisterModule<AOTModule>();
        builder.RegisterSource(new PriorityEnumerationSource());
        if (aotRegisteredLast) builder.RegisterModule<AOTModule>();

        var global = new Plugin();
        var network = new Plugin();
        var child = new Plugin();
        builder.RegisterInstance(global).WithPriority(10);
        builder.RegisterInstance(network).WithMetadata("Network", 42).WithPriority(-10);
        using var container = builder.Build();

        void CheckRoot()
        {
            var views = container.Resolve<IEnumerable<Meta<Plugin, Metadata>>>().ToArray();
            Assert.Equal(new[] { global, network }, views.Select(view => view.Value));
            Assert.Equal(new int?[] { null, 42 }, views.Select(view => view.Metadata.Network));
        }

        if (resolveRootFirst) CheckRoot();
        using var scope = container.BeginLifetimeScope(b =>
            b.RegisterInstance(child).WithMetadata("Network", 7).WithPriority(-20));
        using var nested = scope.BeginLifetimeScope();
        foreach (var current in new[] { scope, nested })
        {
            var views = current.Resolve<IEnumerable<Meta<Plugin, Metadata>>>().ToArray();
            Assert.Equal(new[] { global, network, child }, views.Select(view => view.Value));
            Assert.Equal(new int?[] { null, 42, 7 }, views.Select(view => view.Metadata.Network));
        }
        CheckRoot();

        // Ordinary collections must still honor priorities when both sources are installed.
        Assert.Equal(new[] { child, network, global }, scope.Resolve<IEnumerable<Plugin>>());
    }
}
