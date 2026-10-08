using System.Numerics;
using System.Text.Json.Serialization;
using Aurelian.Combat;
using Aurelian.Runtime;
using Aurelian.Simulation;

namespace Aurelian.Games;

public sealed record Point3(float X, float Y, float Z)
{
    public Vector3 ToVector() => new(X, Y, Z);
    public static Point3 From(Vector3 value) => new(value.X, value.Y, value.Z);
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);
}

public sealed record StarterObject(string Id, Point3 Position, Point3 HalfSize, int Health = 3);
public sealed record StarterOptions(
    string Title = "AURELIAN TRAINING RANGE",
    float MovementSpeed = 4.5f,
    GunConfiguration? Gun = null,
    IReadOnlyList<StarterObject>? Objects = null);
public sealed record StarterSnapshot(Point3 Position, float Yaw, float Pitch, float VerticalVelocity,
    double Time, CameraView View, GunSnapshot Gun, IReadOnlyList<int> ObjectHealth, GameKeyBindings Keys, float Volume,
    IReadOnlyList<CadenceAccumulatorFact> Cadence, bool PendingJump, bool PendingReload, bool PendingFire);
public sealed record StarterObservation(string Screen, Point3 Position, float Yaw, float Pitch, CameraView View,
    int Ammo, bool Reloading, int Shots, int Hits, double Time, string DefinitionIdentity, string? PersistenceError);
public sealed record GameSettings(GameKeyBindings Keys, float Volume);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(StarterSnapshot))]
[JsonSerializable(typeof(StarterObservation))]
[JsonSerializable(typeof(StarterOptions))]
[JsonSerializable(typeof(GameSettings))]
public partial class StarterJsonContext : JsonSerializerContext;
