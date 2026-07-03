using Machina.Presenter.Sample.Playback;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class PlaybackTargetResolverM16aTests
{
    [Fact]
    public void PlaybackTargetResolver_ResolvesMainStack()
    {
        PresenterNavigationShellRenderResult render = PlaybackTestEnvironment.RenderExecutionRoadmapShell();

        PresenterPlaybackResolvedTarget target = PresenterPlaybackTargetResolver.Resolve(render, "main-stack");

        Assert.Equal("main-stack", target.Name);
        Assert.True(target.Bounds.Width > 0);
        Assert.True(target.Bounds.Height > 0);
    }

    [Fact]
    public void PlaybackTargetResolver_ResolvesCardHeaderByCardId()
    {
        PresenterNavigationShellRenderResult render = PlaybackTestEnvironment.RenderDocsShell();

        PresenterPlaybackResolvedTarget target = PresenterPlaybackTargetResolver.Resolve(render, "card-header", "doc-machina-oblivion-phase-closeout-m11g");

        Assert.Equal("doc-machina-oblivion-phase-closeout-m11g", target.CardId);
        Assert.True(target.Bounds.Width > 0);
    }

    [Fact]
    public void PlaybackTargetResolver_ResolvesExpandedBodyByCardId()
    {
        PresenterNavigationShellRenderResult render = PlaybackTestEnvironment.RenderExecutionRoadmapShell(
            expandedCardId: "markdown-first-roadmap",
            inspectorScrollOffset: 240);

        PresenterPlaybackResolvedTarget target = PresenterPlaybackTargetResolver.Resolve(render, "expanded-body", "markdown-first-roadmap");

        Assert.Equal("markdown-first-roadmap", target.CardId);
        Assert.True(target.Bounds.Height >= 120);
    }

    [Fact]
    public void PlaybackTargetResolver_ResolvesInspectorPane()
    {
        PresenterNavigationShellRenderResult render = PlaybackTestEnvironment.RenderExecutionRoadmapShell();

        PresenterPlaybackResolvedTarget target = PresenterPlaybackTargetResolver.Resolve(render, "inspector-pane");

        Assert.Equal("inspector-pane", target.Name);
        Assert.True(target.Bounds.Width > 0);
    }

    [Fact]
    public void PlaybackTargetResolver_ResolvesRawSource()
    {
        PresenterNavigationShellRenderResult render = PlaybackTestEnvironment.RenderDocsShell(
            selectedCardId: "doc-aurelian-build-topology-m13b",
            expandedCardId: "doc-aurelian-build-topology-m13b",
            inspectorScrollOffset: 240,
            width: 1280,
            height: 360);

        PresenterPlaybackResolvedTarget target = PresenterPlaybackTargetResolver.Resolve(
            render,
            "raw-source",
            "doc-aurelian-build-topology-m13b");

        Assert.Equal("doc-aurelian-build-topology-m13b", target.CardId);
        Assert.True(target.Bounds.Height > 0);
    }

    [Fact]
    public void PlaybackTargetResolver_FailsClearlyForUnavailableTarget()
    {
        PresenterNavigationShellRenderResult render = PlaybackTestEnvironment.RenderExecutionRoadmapShell();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => PresenterPlaybackTargetResolver.Resolve(render, "expanded-body", "markdown-first-roadmap"));

        Assert.Contains("unavailable", ex.Message, StringComparison.Ordinal);
    }
}
