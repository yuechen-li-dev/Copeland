using Machina.Runtime.Input;
using Xunit;

namespace Machina.Runtime.Tests.Input;

public sealed class UiInputContractsTests
{
    [Fact]
    public void Batch_PreservesCallerEventOrderAndIsImmutable()
    {
        UiInputEvent[] source =
        [
            new UiPointerMoved(new PointerPoint(2, 3), null, UiModifiers.None),
            new UiPointerWheel(new PointerPoint(2, 3), 0, 1, UiModifiers.None),
            new UiTextEntered("a"),
        ];

        var batch = new UiInputBatch(17, source);
        source[0] = new UiCloseRequested();

        Assert.Equal((ulong)17, batch.BatchId);
        Assert.Collection(
            batch.Events,
            inputEvent => Assert.IsType<UiPointerMoved>(inputEvent),
            inputEvent => Assert.IsType<UiPointerWheel>(inputEvent),
            inputEvent => Assert.IsType<UiTextEntered>(inputEvent));
        Assert.Throws<NotSupportedException>(() => ((IList<UiInputEvent>)batch.Events).Add(new UiCloseRequested()));
    }

    [Fact]
    public void EmptyBatch_HasNoEvents()
    {
        Assert.Empty(UiInputBatch.Empty(4).Events);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Batch_RejectsNonFinitePointerCoordinates(double coordinate)
    {
        Assert.Throws<ArgumentException>(() => new UiInputBatch(1,
        [
            new UiPointerMoved(new PointerPoint(coordinate, 0), null, UiModifiers.None),
        ]));
    }

    [Fact]
    public void TextAndKeyRemainDistinct()
    {
        var batch = new UiInputBatch(2,
        [
            new UiKeyChanged(UiKey.Enter, true, false, UiModifiers.None),
            new UiTextEntered("\r"),
        ]);

        Assert.IsType<UiKeyChanged>(batch.Events[0]);
        Assert.IsType<UiTextEntered>(batch.Events[1]);
    }
}
