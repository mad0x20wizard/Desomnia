using MadWizard.Desomnia.Events;
using Xunit;

namespace MadWizard.Desomnia.Tests.Engine;

public class ActionContextTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Parent_handler_receives_only_its_own_path(bool searchAnotherTreeFirst)
    {
        using var source = new Source();
        using var middle = new Middle();
        using var receiver = new Receiver();
        using var above = new ResourceMonitor<Resource>();
        using var otherMiddle = new Middle();
        using var otherAbove = new ResourceMonitor<Resource>();
        if (searchAnotherTreeFirst)
        {
            otherMiddle.StartTracking(source);
            otherAbove.StartTracking(otherMiddle);
        }
        middle.StartTracking(source);
        receiver.StartTracking(middle);
        above.StartTracking(receiver);

        var demand = ((IEventSystem)source)["Demand"];
        demand.AddAction(new JSEventAction("capture"));
        var notification = new Event();

        await demand.TriggerEventAsync(notification);

        Assert.Same(source, receiver.Arguments.Item1);
        Assert.Same(middle, receiver.Arguments.Item2);
        Assert.Same(receiver, receiver.Arguments.Item3);
        Assert.Same(source, notification.Source);
        Assert.Single(receiver.Context.OfType<Source>());
        Assert.Contains(receiver.Context, item => ReferenceEquals(item, middle));
        Assert.Contains(receiver.Context, item => ReferenceEquals(item, receiver));
        Assert.DoesNotContain(receiver.Context, item => ReferenceEquals(item, above));
        Assert.DoesNotContain(receiver.Context, item => ReferenceEquals(item, otherMiddle));
        Assert.DoesNotContain(receiver.Context, item => ReferenceEquals(item, otherAbove));
        Assert.Empty(notification.Context.OfType<ResourceMonitor<Resource>>());
    }

    [Fact]
    public async Task Concurrent_actions_on_the_same_event_keep_separate_parent_contexts()
    {
        using var source = new Source();
        using var first = new WaitingReceiver();
        using var second = new WaitingReceiver();
        var notification = new Event { Source = source };
        var firstAction = first.TryHandleEventAction(notification, new JSEventAction("wait"));
        var secondAction = second.TryHandleEventAction(notification, new JSEventAction("wait"));

        first.Continue.TrySetResult();
        second.Continue.TrySetResult();
        await Task.WhenAll(firstAction, secondAction).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new object[] { notification, source, first }, first.Context);
        Assert.Equal(new object[] { notification, source, second }, second.Context);
        Assert.Same(first, Assert.Single(first.Context.OfType<WaitingReceiver>()));
        Assert.Same(second, Assert.Single(second.Context.OfType<WaitingReceiver>()));
        Assert.Empty(notification.Context.OfType<WaitingReceiver>());
    }

    private class Source : Resource
    {
        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval) => [];
    }

    private class Middle : ResourceMonitor<Resource> { }

    private class Receiver : ResourceMonitor<Resource>
    {
        public (Source?, Middle?, Receiver?) Arguments;
        public object[] Context = [];

        [ActionHandler("capture")]
        private void Capture(Source source, Middle middle, Receiver receiver, Event notification)
        {
            Arguments = (source, middle, receiver);
            Context = [.. notification.Context];
        }
    }

    private class WaitingReceiver : ResourceMonitor<Resource>
    {
        public TaskCompletionSource Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public object[] Context = [];

        [ActionHandler("wait")]
        private async Task Wait(Event notification)
        {
            await Continue.Task;
            Context = [.. notification.Context];
        }
    }
}
