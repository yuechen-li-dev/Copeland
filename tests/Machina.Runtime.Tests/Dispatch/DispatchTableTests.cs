using Machina.Runtime.Dispatch;
using Xunit;

namespace Machina.Runtime.Tests.Dispatch;

public sealed class DispatchTableTests
{
    [Fact]
    public void Increment_UpdatesCount()
    {
        var table = DispatchTable.For<CounterState>()
            .Increment(
                eventName: "counter.increment",
                get: state => state.Count,
                set: (state, value) => state with { Count = value });

        var next = table.Dispatch(new CounterState(0), "counter.increment");

        Assert.Equal(1, next.Count);
    }

    [Fact]
    public void Increment_WithCustomAmount_UpdatesCount()
    {
        var table = DispatchTable.For<CounterState>()
            .Increment("counter.incrementByThree", s => s.Count, (s, value) => s with { Count = value }, by: 3);

        var next = table.Dispatch(new CounterState(2), "counter.incrementByThree");

        Assert.Equal(5, next.Count);
    }

    [Fact]
    public void Increment_UnknownEvent_ReturnsSameReference()
    {
        var table = DispatchTable.For<CounterState>()
            .Increment("counter.increment", s => s.Count, (s, value) => s with { Count = value });
        var state = new CounterState(1);

        var next = table.Dispatch(state, "unknown");

        Assert.Same(state, next);
    }

    [Fact]
    public void Increment_ByZero_ReturnsSameReference()
    {
        var table = DispatchTable.For<CounterState>()
            .Increment("counter.increment", s => s.Count, (s, value) => s with { Count = value }, by: 0);
        var state = new CounterState(10);

        var next = table.Dispatch(state, "counter.increment");

        Assert.Same(state, next);
    }

    [Fact]
    public void Set_UpdatesValue()
    {
        var table = DispatchTable.For<AppState>()
            .Set(
                eventName: "nav.settings",
                get: state => state.Route,
                set: (state, value) => state with { Route = value },
                value: "settings");

        var next = table.Dispatch(new AppState("home", false, 0), "nav.settings");

        Assert.Equal("settings", next.Route);
    }

    [Fact]
    public void Set_SameValue_ReturnsSameReference()
    {
        var table = DispatchTable.For<AppState>()
            .Set("nav.settings", s => s.Route, (s, value) => s with { Route = value }, "settings");
        var state = new AppState("settings", false, 0);

        var next = table.Dispatch(state, "nav.settings");

        Assert.Same(state, next);
    }

    [Fact]
    public void Toggle_FlipsBoolean()
    {
        var table = DispatchTable.For<AppState>()
            .Toggle("filters.newOnly.toggle", s => s.NewOnly, (s, value) => s with { NewOnly = value });

        var next = table.Dispatch(new AppState("home", false, 0), "filters.newOnly.toggle");

        Assert.True(next.NewOnly);
    }

    [Fact]
    public void FirstMatchWins_WhenEventsDuplicate()
    {
        var table = DispatchTable.For<CounterState>()
            .Increment("counter.increment", s => s.Count, (s, value) => s with { Count = value }, by: 1)
            .Increment("counter.increment", s => s.Count, (s, value) => s with { Count = value }, by: 10);

        var next = table.Dispatch(new CounterState(0), "counter.increment");

        Assert.Equal(1, next.Count);
    }

    [Fact]
    public void Append_IsImmutable()
    {
        var empty = DispatchTable.For<CounterState>();
        var withOne = empty.Increment("counter.increment", s => s.Count, (s, value) => s with { Count = value });
        var state = new CounterState(0);

        var emptyNext = empty.Dispatch(state, "counter.increment");
        var withOneNext = withOne.Dispatch(state, "counter.increment");

        Assert.Same(state, emptyNext);
        Assert.Equal(1, withOneNext.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidTransitionEvent_IsRejected(string? invalidEventName)
    {
        var error = Assert.Throws<MachinaDispatchError>(() =>
            DispatchTable.For<CounterState>()
                .Increment(invalidEventName!, s => s.Count, (s, value) => s with { Count = value }));

        Assert.Equal("InvalidDispatchTransition", error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidDispatchEvent_IsRejected(string? invalidEventName)
    {
        var table = DispatchTable.For<CounterState>()
            .Increment("counter.increment", s => s.Count, (s, value) => s with { Count = value });

        var error = Assert.Throws<MachinaDispatchError>(() => table.Dispatch(new CounterState(0), invalidEventName!));

        Assert.Equal("InvalidDispatchEvent", error.Code);
    }

    [Fact]
    public void NullGetter_IsRejected()
    {
        Func<CounterState, int>? get = null;

        var error = Assert.Throws<MachinaDispatchError>(() =>
            DispatchTable.For<CounterState>()
                .Increment("counter.increment", get!, (s, value) => s with { Count = value }));

        Assert.Equal("InvalidDispatchTransition", error.Code);
    }

    [Fact]
    public void NullSetter_IsRejected()
    {
        Func<CounterState, int, CounterState>? set = null;

        var error = Assert.Throws<MachinaDispatchError>(() =>
            DispatchTable.For<CounterState>()
                .Increment("counter.increment", s => s.Count, set!));

        Assert.Equal("InvalidDispatchTransition", error.Code);
    }

    [Fact]
    public void NullState_IsRejected()
    {
        var table = DispatchTable.For<CounterState>()
            .Increment("counter.increment", s => s.Count, (s, value) => s with { Count = value });

        var error = Assert.Throws<MachinaDispatchError>(() => table.Dispatch(null!, "counter.increment"));

        Assert.Equal("InvalidDispatchValue", error.Code);
    }

    [Fact]
    public void IncrementOverflow_IsRejected()
    {
        var table = DispatchTable.For<CounterState>()
            .Increment("counter.increment", s => s.Count, (s, value) => s with { Count = value }, by: 1);

        var error = Assert.Throws<MachinaDispatchError>(() => table.Dispatch(new CounterState(int.MaxValue), "counter.increment"));

        Assert.Equal("InvalidDispatchValue", error.Code);
    }

    [Fact]
    public void DispatchChain_ProducesExpectedState()
    {
        var table = DispatchTable.For<AppState>()
            .Set("nav.settings", s => s.Route, (s, value) => s with { Route = value }, "settings")
            .Toggle("filters.newOnly.toggle", s => s.NewOnly, (s, value) => s with { NewOnly = value })
            .Increment("cart.increment", s => s.CartCount, (s, value) => s with { CartCount = value });

        var state = new AppState("home", false, 0);
        state = table.Dispatch(state, "nav.settings");
        state = table.Dispatch(state, "filters.newOnly.toggle");
        state = table.Dispatch(state, "cart.increment");

        Assert.Equal("settings", state.Route);
        Assert.True(state.NewOnly);
        Assert.Equal(1, state.CartCount);
    }

    private sealed record CounterState(int Count);

    private sealed record AppState(string Route, bool NewOnly, int CartCount);
}
