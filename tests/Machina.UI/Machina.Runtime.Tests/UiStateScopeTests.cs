using Machina.Runtime.State;
using Xunit;

namespace Machina.Runtime.Tests;

public sealed class UiStateScopeTests
{
    [Fact]
    public void TypedStateKeyCarriesTheValueTypeAndActivationLifetime()
    {
        var key = new UiStateKey<UiState<string>>("search", UiStateLifetime.Activation);
        using var scope = new UiStateScope();
        UiState<string> state = scope.GetOrCreate(key, () => new UiState<string>("query"));
        Assert.Same(state, scope.GetOrCreate(key, () => new UiState<string>("other")));
        scope.Suspend();
        Assert.NotSame(state, scope.GetOrCreate(key, () => new UiState<string>("fresh")));
        Assert.Throws<ArgumentNullException>(() => scope.GetOrCreate(default(UiStateKey<UiState<string>>),
            () => new UiState<string>("invalid")));
    }

    [Fact]
    public void SuspensionForgetsInputCaptureAndPreservesScreenValues()
    {
        using var scope = new UiStateScope();
        object selection = scope.GetOrCreate("selection", () => new object());
        object press = scope.GetOrCreate("pointer", () => new object(), UiStateLifetime.Activation);
        scope.Suspend();
        Assert.Same(selection, scope.GetOrCreate("selection", () => new object()));
        Assert.NotSame(press, scope.GetOrCreate("pointer", () => new object(), UiStateLifetime.Activation));
    }

    [Fact]
    public void DisposalCancelsLifetimeDropsValuesAndRejectsLaterReads()
    {
        var scope = new UiStateScope();
        CancellationToken lifetime = scope.Lifetime;
        scope.GetOrCreate("search", () => new UiState<string>("typed"));
        scope.Dispose();
        scope.Dispose();
        Assert.True(lifetime.IsCancellationRequested);
        Assert.Equal(0, scope.Count);
        Assert.Throws<ObjectDisposedException>(() => scope.GetOrCreate("search", () => new object()));
    }

    [Fact]
    public void ReusingAnIdWithAnotherTypeOrLifetimeReportsTheMistake()
    {
        using var scope = new UiStateScope();
        scope.GetOrCreate("search", () => new UiState<string>(""));
        Assert.Throws<InvalidOperationException>(() => scope.GetOrCreate("search", () => new UiState<int>(0)));
        Assert.Throws<InvalidOperationException>(() => scope.GetOrCreate("search",
            () => new UiState<string>(""), UiStateLifetime.Activation));
    }
}
