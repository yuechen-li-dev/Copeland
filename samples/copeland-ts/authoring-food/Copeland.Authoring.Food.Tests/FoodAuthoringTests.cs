using Copeland.Authoring.Food.Copeland;
using Xunit;

namespace Copeland.Authoring.Food.Tests;

public sealed class FoodAuthoringTests
{
    [Fact]
    public void Recipe_summary_uses_record_union_clr_call_and_inline_csharp()
    {
        string summary = Main.Run(" lentil stew ", 4, 560);

        Assert.Equal("[LENTIL STEW] serves 4 for 560 calories", summary);
    }

    [Fact]
    public void Batch_and_generator_produce_ordered_values()
    {
        Assert.Equal([2d, 4d, 6d], Main.DoubledPortions([1d, 2d, 3d]));
        Assert.Equal(3d, Main.CookingSlotTotal(3));
        Assert.Equal(9d, Main.PlanPortions(4));
    }

    [Fact]
    public void Flow_commits_board_updates_before_completion()
    {
        var pantry = PantryRun.Start();

        pantry.SendAdd(2);
        pantry.SendAdd(3);
        var completion = pantry.SendClose();

        Assert.Equal("Completed", completion.Kind);
        Assert.True(completion.IsTerminal);
        Assert.Equal(3, pantry.Revision);
    }
}
