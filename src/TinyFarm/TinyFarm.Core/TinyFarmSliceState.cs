namespace TinyFarm.Core;

public sealed record SliceTickIntent : GameIntent;
public sealed record SwordIntent : GameIntent;
public sealed record DodgeIntent(int X, int Y) : GameIntent;
public sealed record EatIntent : GameIntent;
public sealed record SleepIntent : GameIntent;

public enum SlimePhase
{
    Rest,
    Watch,
    Windup,
    Lunge,
    Recover
}

/// <summary>Immutable, persisted opening-slice combat truth. Sprite poses are derived from these ticks.</summary>
public sealed record TinyFarmSliceState(
    long Tick,
    int Health,
    int HurtTicks,
    int SwordTicks,
    int DodgeTicks,
    int DodgeCooldown,
    ScenePosition DodgeDirection,
    ScenePosition SlimePosition,
    SlimePhase SlimePhase,
    int SlimeTicks,
    ScenePosition SlimeDirection,
    bool SwordHit,
    bool OwnHarvest,
    bool Slept,
    int Defeats,
    bool CookedBroth = false,
    bool SwordBuffered = false,
    bool ReturnedHome = false)
{
    public static TinyFarmSliceState Start(ScenePosition slime) => new(
        0, 12, 0, 0, 0, 0, default, slime, SlimePhase.Watch, 18, default,
        false, false, false, 0);

    public bool LoopComplete => OwnHarvest && Slept && CookedBroth && Defeats > 0 && ReturnedHome;
}
