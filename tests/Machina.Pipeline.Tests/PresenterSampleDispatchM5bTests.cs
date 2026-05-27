using Xunit;
using Machina.Core.Actions;
using Machina.Presenter.Sample;

namespace Machina.Pipeline.Tests;

public sealed class PresenterSampleDispatchM5bTests
{
    [Fact]
    public void PresenterDispatch_Increment_IncrementsCount()
    {
        var state = new DemoState(0, EmailUpdates: true, Notifications: false);

        var next = DemoStateDispatch.Dispatch(state, DemoDocumentFactory.Actions.Increment);

        Assert.Equal(1, next.Count);
        Assert.True(next.EmailUpdates);
        Assert.False(next.Notifications);
        Assert.False(ReferenceEquals(state, next));
    }

    [Fact]
    public void PresenterDispatch_ToggleEmailUpdates_TogglesOnlyEmail()
    {
        var state = new DemoState(3, EmailUpdates: true, Notifications: false);

        var next = DemoStateDispatch.Dispatch(state, DemoDocumentFactory.Actions.ToggleEmailUpdates);

        Assert.Equal(3, next.Count);
        Assert.False(next.EmailUpdates);
        Assert.False(next.Notifications);
        Assert.False(ReferenceEquals(state, next));
    }

    [Fact]
    public void PresenterDispatch_ToggleNotifications_TogglesOnlyNotifications()
    {
        var state = new DemoState(4, EmailUpdates: true, Notifications: false);

        var next = DemoStateDispatch.Dispatch(state, DemoDocumentFactory.Actions.ToggleNotifications);

        Assert.Equal(4, next.Count);
        Assert.True(next.EmailUpdates);
        Assert.True(next.Notifications);
        Assert.False(ReferenceEquals(state, next));
    }

    [Fact]
    public void PresenterDispatch_UnknownAction_ReturnsSameState()
    {
        var state = new DemoState(7, EmailUpdates: false, Notifications: true);

        var next = DemoStateDispatch.Dispatch(state, new UiActionId("unknown.action"));

        Assert.Same(state, next);
        Assert.Equal(state, next);
    }
}
