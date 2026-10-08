using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Network.Environments;
using MadWizard.Desomnia.Network.Interface;
using MadWizard.Desomnia.Network.Manager;
using System.Net.NetworkInformation;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests
{
    public class InterfaceConditionTests
    {
        [Fact]
        public void WithoutSuffix_MatchesAnyOperationalStatus()
        {
            var (pattern, statuses) = InterfaceCondition.Parse("en0");

            Assert.Equal("en0", pattern);
            Assert.Null(statuses); // null accepts every status - mere presence
        }

        [Fact]
        public void SingleStatus_IsParsed()
        {
            var (pattern, statuses) = InterfaceCondition.Parse("en0@up");

            Assert.Equal("en0", pattern);
            Assert.Equal([OperationalStatus.Up], statuses);
        }

        [Fact]
        public void MultipleStatuses_AreParsed()
        {
            var (pattern, statuses) = InterfaceCondition.Parse("en0@up|down|dormant");

            Assert.Equal("en0", pattern);
            Assert.Equal([OperationalStatus.Up, OperationalStatus.Down, OperationalStatus.Dormant], statuses);
        }

        [Theory]
        [InlineData("en0@UP")]
        [InlineData("en0@Up")]
        [InlineData("en0@ up ")]
        [InlineData("en0@up|UP")] // duplicates collapse
        internal void StatusNames_AreCaseInsensitiveAndTrimmed(string value)
        {
            Assert.Equal([OperationalStatus.Up], InterfaceCondition.Parse(value).Statuses);
        }

        [Fact]
        public void CompoundStatusNames_AreAccepted()
        {
            var (_, statuses) = InterfaceCondition.Parse("en0@notpresent|lowerlayerdown");

            Assert.Equal([OperationalStatus.NotPresent, OperationalStatus.LowerLayerDown], statuses);
        }

        [Fact]
        public void RegexAlternation_StaysPartOfThePattern()
        {
            // '|' only separates statuses behind the '@' - before it, it is regex alternation
            var (pattern, statuses) = InterfaceCondition.Parse("^(en0|en12)$@up");

            Assert.Equal("^(en0|en12)$", pattern);
            Assert.Equal([OperationalStatus.Up], statuses);
        }

        [Fact]
        public void PatternMayContainTheSeparator()
        {
            // the split happens at the LAST separator
            var (pattern, statuses) = InterfaceCondition.Parse("foo@bar@up");

            Assert.Equal("foo@bar", pattern);
            Assert.Equal([OperationalStatus.Up], statuses);
        }

        [Theory]
        [InlineData("en0@")]              // no status behind the separator
        [InlineData("en0@up|")]           // trailing delimiter
        [InlineData("en0@online")]        // not an OperationalStatus
        [InlineData("en0@up|onilne")]     // typo in a list
        [InlineData("en0@1")]             // numbers must not slip through Enum.TryParse
        [InlineData("en0@up,down")]       // comma lists must not slip through either
        [InlineData("@up")]               // empty pattern
        [InlineData("[unclosed@up")]      // invalid regex
        public void InvalidValue_Throws(string value)
        {
            Assert.Throws<ConfigurationValueException>(() => InterfaceCondition.Parse(value));
        }

        [Fact]
        public void InvalidStatus_MessageListsTheValidOnes()
        {
            var exception = Assert.Throws<ConfigurationValueException>(() => InterfaceCondition.Parse("en0@online"));

            Assert.Contains("online", exception.Message);
            Assert.Contains("up", exception.Message);
            Assert.Contains("lowerlayerdown", exception.Message);
        }

        [Fact]
        public void Interface_IsMatchedByPresenceAndByStatus()
        {
            var manager = new FakeNetworkInterfaceManager(
                new FakeNetworkInterface("en0") { Status = OperationalStatus.Dormant });

            Assert.True(new InterfaceCondition(new(), "en0") { Manager = manager }.IsSatisfied());
            Assert.True(new InterfaceCondition(new(), "en0@dormant") { Manager = manager }.IsSatisfied());
            Assert.False(new InterfaceCondition(new(), "en0@up") { Manager = manager }.IsSatisfied());
            Assert.False(new InterfaceCondition(new(), "definitely-no-such-interface") { Manager = manager }.IsSatisfied());
        }

        [Fact]
        public void ProvidedMatcher_DecidesHowTheInterfaceIsMatched()
        {
            // what the Windows host does with the display name, here against a fixed answer:
            // the condition has to route its matching through the matcher it was given
            var manager = new FakeNetworkInterfaceManager(new FakeNetworkInterface("en0"));

            Assert.True(new InterfaceCondition(new AlwaysMatcher(), "anything") { Manager = manager }.IsSatisfied());
            Assert.False(new InterfaceCondition(new NeverMatcher(), "anything") { Manager = manager }.IsSatisfied());
        }

        [Fact]
        public void Changed_ForwardsTheManagersChanges_OnlyWhileSubscribed()
        {
            var manager = new FakeNetworkInterfaceManager(new FakeNetworkInterface("en0"));

            var condition = new InterfaceCondition(new(), "en0") { Manager = manager };

            int raised = 0;
            EventHandler handler = (_, _) => raised++;

            Assert.False(manager.HasChangedSubscribers); // lazy — nothing until someone listens

            condition.Changed += handler;

            Assert.True(manager.HasChangedSubscribers);

            manager.RaiseChanged();

            Assert.Equal(1, raised);

            condition.Changed -= handler;

            Assert.False(manager.HasChangedSubscribers);
        }

        private sealed class AlwaysMatcher : InterfaceMatcher
        {
            protected override bool MatchesInterface(INetworkInterface @interface, string pattern) => true;
        }

        private sealed class NeverMatcher : InterfaceMatcher
        {
            protected override bool MatchesInterface(INetworkInterface @interface, string pattern) => false;
        }
    }
}
