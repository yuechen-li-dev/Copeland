using Machina.Core.Actions;
using Machina.Core.Nodes;
using Machina.Layout.Rows;
using Xunit;

namespace Machina.Core.Tests;

public sealed class TypedUiIdentityTests
{
    [Fact]
    public void TypedActionsKeepTheirMessagesAndCompareTheirValues()
    {
        UiActionId id = new("choose");
        UiAction first = UiAction.Typed<Message>(id, new Message.Choose(new RecipeId("first")));
        UiAction equivalent = UiAction.Typed<Message>(id, new Message.Choose(new RecipeId("first")));
        UiAction changed = UiAction.Typed<Message>(id, new Message.Choose(new RecipeId("second")));
        Assert.Equal(first, equivalent);
        Assert.NotEqual(first, changed);
        Assert.NotEqual(first, UiAction.Named(id));
        Assert.True(first.TryGetMessage<Message>(out var message));
        Assert.Equal(new Message.Choose(new RecipeId("first")), message);
        Assert.False(UiAction.Named(id).TryGetMessage<Message>(out _));
        Assert.False(first.TryGetMessage<string>(out _));
        Assert.Equal(2, new HashSet<UiAction> { first, equivalent, changed }.Count);
    }

    [Theory]
    [InlineData("a.b")]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("a%2Eb")]
    [InlineData("a b")]
    [InlineData("recette/茶")]
    public void KeyedIdsAndActionAliasesUseCanonicalEscapedSegments(string value)
    {
        var nodes = new UiIdFamily<RecipeId>("recipes", key => key.Value);
        var actions = new UiActionFamily<RecipeId, Message>(new UiActionId("recipe"), key => key.Value,
            key => new Message.Choose(key));
        var key = new RecipeId(value);
        Assert.Equal(new NodeId("recipes").Child(value), nodes[key]);
        Assert.Equal(new UiActionId("recipe").Child(value), actions[key].Id);
        Assert.Equal(new Message.Choose(key), actions[key].Message);
        Assert.True(nodes[key].IsWithin("recipes"));
        Assert.False(nodes[key].IsWithin("recipe"));
    }

    [Fact]
    public void SegmentBoundariesAndLiteralEscapesCannotCollide()
    {
        NodeId root = new("root");
        Assert.NotEqual(root.Child("a.b"), root.Child("a").Child("b"));
        Assert.NotEqual(root.Child("a.b"), root.Child("a%2Eb"));
        UiActionId action = new("root");
        Assert.NotEqual(action.Child("a:b"), action.Child("a").Child("b"));
        Assert.NotEqual(action.Child("a:b"), action.Child("a%3Ab"));
    }

    [Fact]
    public void NumericChildIdentityUsesInvariantFormatting()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        var unusual = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
        unusual.NumberFormat.NegativeSign = "negative";
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = unusual;
            Assert.Equal("rows.-1", new NodeId("rows").Child(-1).Value);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void EmptyOrDefaultTypedIdentitiesFailAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new NodeId("root").Child(""));
        Assert.Throws<ArgumentNullException>(() => new UiActionFamily<RecipeId, Message>(default,
            key => key.Value, key => new Message.Choose(key)));
        Assert.Throws<ArgumentNullException>(() => UiAction.Typed(default, "payload"));
    }

    private readonly record struct RecipeId(string Value);
    private abstract record Message
    {
        public sealed record Choose(RecipeId Id) : Message;
    }
}
