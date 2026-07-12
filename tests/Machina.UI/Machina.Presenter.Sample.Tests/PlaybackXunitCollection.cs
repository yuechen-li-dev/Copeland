using Xunit;

namespace Machina.Presenter.Sample.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PlaybackXunitCollection : ICollectionFixture<PlaybackXunitCollectionFixture>
{
    public const string Name = "Machina Playback xUnit";
}

public sealed class PlaybackXunitCollectionFixture
{
}
