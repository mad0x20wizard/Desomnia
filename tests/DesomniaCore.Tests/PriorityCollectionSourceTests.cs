using Autofac;
using Autofac.Features.Metadata;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    public class PriorityCollectionSourceTests
    {
        public interface IDetector { }

        // registered in the order A, B, C, D — so any order below is the source's doing, not the registry's
        public sealed class DetectorA : IDetector { }
        public sealed class DetectorB : IDetector { }
        public sealed class DetectorC : IDetector { }
        public sealed class DetectorD : IDetector { }
        public sealed class DetectorE : IDetector { }
        public sealed class DetectorF : IDetector { }

        public interface IPlain { }
        public sealed class PlainA : IPlain { }
        public sealed class PlainB : IPlain { }

        public interface IWrapped { }
        public sealed class Wrapped : IWrapped { }
        public sealed class WrappedDecorator(IWrapped inner) : IWrapped
        {
            public IWrapped Inner => inner;
        }

        public sealed class Consumer(IEnumerable<IDetector> detectors)
        {
            public IEnumerable<IDetector> Detectors => detectors;
        }

        public sealed class ArrayConsumer(IDetector[] detectors)
        {
            public IDetector[] Detectors => detectors;
        }

        private static IContainer Build(Action<ContainerBuilder>? configure = null)
        {
            var builder = new ContainerBuilder();

            builder.RegisterSource(new PriorityCollectionSource());

            builder.RegisterType<DetectorA>().As<IDetector>().WithPriority(2);
            builder.RegisterType<DetectorB>().As<IDetector>();          // no priority — counts as 0
            builder.RegisterType<DetectorC>().As<IDetector>().WithPriority(-1);
            builder.RegisterType<DetectorD>().As<IDetector>().WithPriority(1);

            builder.RegisterType<Consumer>().AsSelf();
            builder.RegisterType<ArrayConsumer>().AsSelf();

            configure?.Invoke(builder);

            return builder.Build();
        }

        private static string[] Names(IEnumerable<IDetector> detectors)
            => [.. detectors.Select(detector => detector.GetType().Name)];

        [Fact]
        public void ResolvingAPlainEnumerable_AppliesThePriorities()
        {
            using var container = Build();

            Assert.Equal(["DetectorC", "DetectorB", "DetectorD", "DetectorA"],
                         Names(container.Resolve<IEnumerable<IDetector>>()));
        }

        [Theory]
        [InlineData(typeof(IEnumerable<IDetector>))]
        [InlineData(typeof(IDetector[]))]
        [InlineData(typeof(IList<IDetector>))]
        [InlineData(typeof(ICollection<IDetector>))]
        [InlineData(typeof(IReadOnlyList<IDetector>))]
        [InlineData(typeof(IReadOnlyCollection<IDetector>))]
        public void EveryCollectionShape_IsOrdered(Type collectionType)
        {
            using var container = Build();

            Assert.Equal(["DetectorC", "DetectorB", "DetectorD", "DetectorA"],
                         Names((IEnumerable<IDetector>) container.Resolve(collectionType)));
        }

        [Fact]
        public void InjectedCollections_AreOrderedToo()
        {
            using var container = Build();

            Assert.Equal(["DetectorC", "DetectorB", "DetectorD", "DetectorA"],
                         Names(container.Resolve<Consumer>().Detectors));
            Assert.Equal(["DetectorC", "DetectorB", "DetectorD", "DetectorA"],
                         Names(container.Resolve<ArrayConsumer>().Detectors));
        }

        /// <summary>
        /// The registry hands its registrations back newest-first, so an unprioritised collection is
        /// where a missing registration-order tie-break would show up as a silently reversed list.
        /// </summary>
        [Fact]
        public void WithoutAnyPriority_RegistrationOrderIsKept()
        {
            using var container = Build(builder =>
            {
                builder.RegisterType<PlainA>().As<IPlain>();
                builder.RegisterType<PlainB>().As<IPlain>();
            });

            Assert.Equal(["PlainA", "PlainB"],
                         container.Resolve<IEnumerable<IPlain>>().Select(plain => plain.GetType().Name));
        }

        [Fact]
        public void EqualPriorities_KeepTheirRegistrationOrder()
        {
            using var container = Build(builder =>
            {
                builder.RegisterType<PlainA>().As<IPlain>().WithPriority(5);
                builder.RegisterType<PlainB>().As<IPlain>().WithPriority(5);
            });

            Assert.Equal(["PlainA", "PlainB"],
                         container.Resolve<IEnumerable<IPlain>>().Select(plain => plain.GetType().Name));
        }

        /// <summary>
        /// Where the priorities actually live: the network, host and service scopes register their
        /// discoveries and services in a child scope, and those have to sort in among the ones the
        /// root container already knows.
        /// </summary>
        [Fact]
        public void ChildScopeRegistrations_SortInAmongTheParentsOwn()
        {
            using var container = Build();
            using var scope = container.BeginLifetimeScope(
                builder => builder.RegisterType<DetectorE>().As<IDetector>().WithPriority(-50));

            Assert.Equal(["DetectorE", "DetectorC", "DetectorB", "DetectorD", "DetectorA"],
                         Names(scope.Resolve<IEnumerable<IDetector>>()));

            Assert.Equal(["DetectorC", "DetectorB", "DetectorD", "DetectorA"],
                         Names(container.Resolve<IEnumerable<IDetector>>()));
        }

        /// <summary>
        /// Autofac answers the list and collection interfaces with a real <see cref="List{T}"/> and only
        /// <see cref="IEnumerable{T}"/> and <c>T[]</c> with an array. Handing back an array everywhere
        /// looks identical until somebody adds to it — a fixed-size list throws.
        /// </summary>
        [Fact]
        public void ListShapes_AreMutableJustAsAutofacsOwnAre()
        {
            using var container = Build();

            container.Resolve<IList<IDetector>>().Add(new DetectorE());
            container.Resolve<ICollection<IDetector>>().Add(new DetectorE());

            Assert.IsType<IDetector[]>(container.Resolve<IEnumerable<IDetector>>());
            Assert.IsType<IDetector[]>(container.Resolve<IDetector[]>());
        }

        /// <summary>
        /// Autofac's own source is per-scope; this one is not, so the case where the root has already
        /// resolved (and cached) the collection before a child scope adds to it has to be pinned down.
        /// </summary>
        [Fact]
        public void CollectionResolvedInTheRootFirst_DoesNotStaleTheChildScope()
        {
            using var container = Build();

            Assert.Equal(4, container.Resolve<IEnumerable<IDetector>>().Count());

            using var scope = container.BeginLifetimeScope(
                builder => builder.RegisterType<DetectorE>().As<IDetector>().WithPriority(-50));

            Assert.Equal(["DetectorE", "DetectorC", "DetectorB", "DetectorD", "DetectorA"],
                         Names(scope.Resolve<IEnumerable<IDetector>>()));
        }

        /// <summary>
        /// The network → host → request nesting: every level of configured scope adds its own
        /// prioritized registrations, and a plain tagged scope (no registrations of its own — the
        /// request scope shape) shares its parent's view. Each level sees exactly its own lineage,
        /// ordered — the single source-created registration enumerates through whichever scope is
        /// resolving.
        /// </summary>
        [Fact]
        public void NestedScopes_EachLevelSeesItsOwnLineageOrdered()
        {
            using var container = Build();
            using var network = container.BeginLifetimeScope("Network",
                builder => builder.RegisterType<DetectorE>().As<IDetector>().WithPriority(-50));
            using var host = network.BeginLifetimeScope("NetworkHost",
                builder => builder.RegisterType<DetectorF>().As<IDetector>().WithPriority(50));
            using var request = host.BeginLifetimeScope("Request");

            Assert.Equal(["DetectorE", "DetectorC", "DetectorB", "DetectorD", "DetectorA", "DetectorF"],
                         Names(host.Resolve<IEnumerable<IDetector>>()));
            Assert.Equal(["DetectorE", "DetectorC", "DetectorB", "DetectorD", "DetectorA", "DetectorF"],
                         Names(request.Resolve<IEnumerable<IDetector>>()));

            Assert.Equal(["DetectorE", "DetectorC", "DetectorB", "DetectorD", "DetectorA"],
                         Names(network.Resolve<IEnumerable<IDetector>>()));
            Assert.Equal(["DetectorC", "DetectorB", "DetectorD", "DetectorA"],
                         Names(container.Resolve<IEnumerable<IDetector>>()));
        }

        [Fact]
        public void KeyedCollections_StayKeyed()
        {
            using var container = Build(builder =>
            {
                builder.RegisterType<DetectorA>().Keyed<IDetector>("k").WithPriority(5);
                builder.RegisterType<DetectorC>().Keyed<IDetector>("k").WithPriority(-5);
            });

            Assert.Equal(["DetectorC", "DetectorA"],
                         Names(container.ResolveKeyed<IEnumerable<IDetector>>("k")));
        }

        [Fact]
        public void NothingRegistered_ResolvesToAnEmptyCollection()
        {
            using var container = Build();

            Assert.Empty(container.Resolve<IEnumerable<IPlain>>());
        }

        /// <summary>Taking over the relationship must not cost the behaviour that came with it.</summary>
        [Fact]
        public void Decorators_AreStillApplied()
        {
            using var container = Build(builder =>
            {
                builder.RegisterType<Wrapped>().As<IWrapped>();
                builder.RegisterDecorator<WrappedDecorator, IWrapped>();
            });

            var wrapped = Assert.Single(container.Resolve<IEnumerable<IWrapped>>());

            Assert.IsType<Wrapped>(Assert.IsType<WrappedDecorator>(wrapped).Inner);
        }

        /// <summary>
        /// The relationship wrappers keep no priority of their own, so they stay on Autofac's own
        /// collection behaviour: registration order.
        /// </summary>
        [Fact]
        public void MetaCollections_StayInRegistrationOrder()
        {
            using var container = Build();

            Assert.Equal(["DetectorA", "DetectorB", "DetectorC", "DetectorD"],
                         container.Resolve<IEnumerable<Meta<IDetector>>>()
                                  .Select(meta => meta.Value.GetType().Name));
        }
    }
}
