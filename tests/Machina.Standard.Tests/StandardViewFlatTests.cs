using Machina.Core.Actions;
using Machina.Core.Flat;
using Machina.Core.Semantics;
using Machina.Standard.Authoring;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class StandardViewFlatTests
{
    [Fact]
    public void StandardViewButton_LowersActionAndSemantics()
    {
        var lowered = UiDocumentLowerer.Lower(UiDocument.Create([Row.Root("button", StandardView.Button("Save", UiAction.Named("save")))]));

        Assert.Equal("save", lowered.Actions["button"].Name);
        Assert.Equal(UiRole.Button, lowered.Semantics["button"].Role);
    }
}
