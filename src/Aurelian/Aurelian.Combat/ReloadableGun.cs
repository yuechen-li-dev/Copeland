namespace Aurelian.Combat;

public sealed record GunConfiguration(int Capacity = 12, float ShotInterval = 0.16f, float ReloadSeconds = 1.1f);
public sealed record GunSnapshot(int Ammo, float ShotCooldown, float ReloadRemaining, int Shots);

/// <summary>Explicit magazine/reload state. A successful shot is a fact for the game to resolve.</summary>
public sealed class ReloadableGun
{
    public ReloadableGun(GunConfiguration? configuration = null)
    {
        Configuration = configuration ?? new();
        if (Configuration.Capacity < 1 || !float.IsFinite(Configuration.ShotInterval) || Configuration.ShotInterval <= 0 ||
            !float.IsFinite(Configuration.ReloadSeconds) || Configuration.ReloadSeconds <= 0)
        {
            throw new ArgumentException("Gun capacity and finite timing values must be positive.");
        }
        State = new(Configuration.Capacity, 0, 0, 0);
    }

    public GunConfiguration Configuration { get; }
    public GunSnapshot State { get; private set; }

    public bool Step(bool fire, bool reload, float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }
        float remaining = MathF.Max(0, State.ReloadRemaining - seconds);
        int ammo = State.ReloadRemaining > 0 && remaining == 0 ? Configuration.Capacity : State.Ammo;
        float cooldown = MathF.Max(0, State.ShotCooldown - seconds);
        if (reload && ammo < Configuration.Capacity && remaining == 0)
        {
            remaining = Configuration.ReloadSeconds;
        }
        bool fired = fire && cooldown == 0 && remaining == 0 && ammo > 0;
        State = new(ammo - (fired ? 1 : 0), fired ? Configuration.ShotInterval : cooldown,
            remaining, State.Shots + (fired ? 1 : 0));
        return fired;
    }

    public void Validate(GunSnapshot snapshot)
    {
        if (snapshot.Ammo < 0 || snapshot.Ammo > Configuration.Capacity || snapshot.Shots < 0 ||
            !float.IsFinite(snapshot.ShotCooldown) || snapshot.ShotCooldown < 0 || snapshot.ShotCooldown > Configuration.ShotInterval ||
            !float.IsFinite(snapshot.ReloadRemaining) || snapshot.ReloadRemaining < 0 || snapshot.ReloadRemaining > Configuration.ReloadSeconds)
        {
            throw new InvalidDataException("Invalid magazine or weapon timer in save.");
        }
    }

    public void Restore(GunSnapshot snapshot)
    {
        Validate(snapshot);
        State = snapshot;
    }
}
