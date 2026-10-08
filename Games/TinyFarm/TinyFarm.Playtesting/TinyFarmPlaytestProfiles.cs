using Aurelian.Playtesting;

namespace TinyFarm.Playtesting;

/// <summary>Preserves TinyFarm's original xorshift corpus through the shared runner.</summary>
public static class TinyFarmPlaytestProfiles
{
    public static PlaytestScript Fuzz(uint seed, int count)
    {
        return PlaytestScripts.Fuzz(seed, count, new PlaytestFuzzProfile(
            ["w", "a", "s", "d", "e", "i", "c", "escape", "enter", "j", "space", "f9"],
            [new("command", Command: "new game")],
            ["w", "s"]));
    }
}
