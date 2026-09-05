using System.Numerics;

namespace Aurelian.Effects2D;

public readonly record struct ParticleSnapshot(
    EmitterInstanceId EmitterId,
    VisualEffectId EffectId,
    Vector2 Position,
    Vector2 Velocity,
    float AgeSeconds,
    float LifetimeSeconds,
    float Size,
    float Rotation,
    uint Variant,
    ulong Seed,
    EffectPainterLayer PainterLayer,
    EffectBlendMode BlendMode,
    EffectCoordinateSpace Space);

public readonly record struct EffectQuadSnapshot(
    EmitterInstanceId EmitterId,
    VisualEffectId EffectId,
    EffectMaterialId MaterialId,
    Vector2 Position,
    float AgeSeconds,
    float LifetimeSeconds,
    float Radius,
    float Intensity,
    ulong Seed,
    EffectPainterLayer PainterLayer,
    EffectBlendMode BlendMode,
    EffectCoordinateSpace Space);

public sealed record ActiveEmitterFact(
    EmitterInstanceId InstanceId,
    VisualEffectEventId EventId,
    VisualEffectId EffectId,
    float AgeSeconds,
    float LifetimeSeconds,
    int ParticleCount,
    ulong Seed,
    EffectMaterialId MaterialId);

public sealed record EffectRuntimeInspection(
    IReadOnlyList<ActiveEmitterFact> ActiveEmitters,
    int ParticleCount,
    long DroppedEffectCount,
    int DedupeCount);

public sealed class EffectRuntime
{
    private readonly EffectCatalog catalog;
    private readonly int particleCapacity;
    private readonly int emitterCapacity;
    private readonly int dedupeCapacity;
    private readonly List<EmitterState> emitters;
    private readonly List<ParticleState> particles;
    private readonly HashSet<VisualEffectEventId> realizedEvents = [];
    private readonly Queue<VisualEffectEventId> realizedOrder = [];
    private long nextInstance;

    public EffectRuntime(
        EffectCatalog catalog,
        int particleCapacity = 2048,
        int emitterCapacity = 256,
        int dedupeCapacity = 4096)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (particleCapacity <= 0 || emitterCapacity <= 0 || dedupeCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(particleCapacity), "Effect capacities must be positive.");
        }
        this.catalog = catalog;
        this.particleCapacity = particleCapacity;
        this.emitterCapacity = emitterCapacity;
        this.dedupeCapacity = dedupeCapacity;
        emitters = new List<EmitterState>(Math.Min(emitterCapacity, 256));
        particles = new List<ParticleState>(Math.Min(particleCapacity, 2048));
    }

    public int ActiveEmitterCount => emitters.Count;

    public int ActiveParticleCount => particles.Count;

    public long DroppedEffectCount { get; private set; }

    public bool TryEmit(VisualEffectEvent effectEvent, out string? diagnostic)
    {
        if (!ValidateEvent(effectEvent, out diagnostic))
        {
            DroppedEffectCount++;
            return false;
        }
        if (realizedEvents.Contains(effectEvent.StableEventId))
        {
            diagnostic = "The visual effect event was already realized.";
            return false;
        }
        RememberEvent(effectEvent.StableEventId);
        EffectDefinition definition = catalog.Get(effectEvent.EffectId);
        int requiredParticles = definition.EmitterKind == EffectEmitterKind.Burst
            ? definition.SpawnCount
            : 0;
        if (emitters.Count >= emitterCapacity || particles.Count + requiredParticles > particleCapacity)
        {
            DroppedEffectCount++;
            diagnostic = "Effect capacity is exhausted; the newest request was rejected.";
            return false;
        }

        var instanceId = new EmitterInstanceId($"effect-instance-{nextInstance++:D12}");
        var emitter = new EmitterState(instanceId, effectEvent, definition);
        emitters.Add(emitter);
        if (definition.EmitterKind == EffectEmitterKind.Burst)
        {
            for (int index = 0; index < definition.SpawnCount; index++)
            {
                particles.Add(Spawn(emitter, index));
            }
            emitter.SpawnOrdinal = definition.SpawnCount;
        }
        diagnostic = null;
        return true;
    }

    public void Update(TimeSpan elapsed)
    {
        double seconds = elapsed.TotalSeconds;
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed), "Effect elapsed time must be finite and non-negative.");
        }
        float delta = (float)seconds;
        for (int index = particles.Count - 1; index >= 0; index--)
        {
            ParticleState particle = particles[index];
            particle.AgeSeconds += delta;
            if (particle.AgeSeconds >= particle.LifetimeSeconds)
            {
                particles.RemoveAt(index);
                continue;
            }
            particle.Position += particle.Velocity * delta;
            particles[index] = particle;
        }

        for (int index = emitters.Count - 1; index >= 0; index--)
        {
            EmitterState emitter = emitters[index];
            emitter.AgeSeconds += delta;
            if (emitter.AgeSeconds >= (float)emitter.Definition.Lifetime.TotalSeconds)
            {
                emitters.RemoveAt(index);
                continue;
            }
            if (emitter.Definition.EmitterKind is EffectEmitterKind.Ambient or EffectEmitterKind.Trail)
            {
                emitter.SpawnDebt += delta * emitter.Definition.SpawnsPerSecond;
                while (emitter.SpawnDebt >= 1 && particles.Count < particleCapacity)
                {
                    particles.Add(Spawn(emitter, emitter.SpawnOrdinal++));
                    emitter.SpawnDebt -= 1;
                }
                if (emitter.SpawnDebt >= 1 && particles.Count >= particleCapacity)
                {
                    emitter.SpawnDebt = 0;
                    DroppedEffectCount++;
                }
            }
        }
    }

    public void Stop(VisualEffectEventId eventId)
    {
        emitters.RemoveAll(emitter => emitter.Event.StableEventId == eventId);
    }

    public IReadOnlyList<ParticleSnapshot> BuildParticleDrawData()
    {
        var result = new ParticleSnapshot[particles.Count];
        for (int index = 0; index < particles.Count; index++)
        {
            ParticleState particle = particles[index];
            result[index] = particle.Snapshot;
        }
        Array.Sort(result, static (left, right) =>
        {
            int layer = left.PainterLayer.CompareTo(right.PainterLayer);
            return layer != 0 ? layer : StringComparer.Ordinal.Compare(left.EmitterId.Value, right.EmitterId.Value);
        });
        return result;
    }

    public IReadOnlyList<EffectQuadSnapshot> BuildQuadDrawData()
    {
        return emitters
            .Where(emitter => emitter.Definition.ShaderQuad
                || emitter.Definition.EmitterKind == EffectEmitterKind.ScreenFlash)
            .OrderBy(emitter => emitter.Definition.PainterLayer)
            .ThenBy(emitter => emitter.InstanceId.Value, StringComparer.Ordinal)
            .Select(emitter => new EffectQuadSnapshot(
                emitter.InstanceId,
                emitter.Event.EffectId,
                emitter.Definition.MaterialId,
                emitter.Event.Position ?? Vector2.Zero,
                emitter.AgeSeconds,
                (float)emitter.Definition.Lifetime.TotalSeconds,
                emitter.Definition.MaximumSize * emitter.Event.Scale,
                emitter.Event.Intensity,
                emitter.Event.Seed,
                emitter.Definition.PainterLayer,
                emitter.Definition.BlendMode,
                emitter.Event.Space))
            .ToArray();
    }

    public EffectRuntimeInspection Inspect()
    {
        ActiveEmitterFact[] facts = emitters
            .OrderBy(emitter => emitter.InstanceId.Value, StringComparer.Ordinal)
            .Select(emitter => new ActiveEmitterFact(
                emitter.InstanceId,
                emitter.Event.StableEventId,
                emitter.Event.EffectId,
                emitter.AgeSeconds,
                (float)emitter.Definition.Lifetime.TotalSeconds,
                particles.Count(particle => particle.EmitterId == emitter.InstanceId),
                emitter.Event.Seed,
                emitter.Definition.MaterialId))
            .ToArray();
        return new EffectRuntimeInspection(facts, particles.Count, DroppedEffectCount, realizedEvents.Count);
    }

    private bool ValidateEvent(VisualEffectEvent effectEvent, out string? diagnostic)
    {
        if (string.IsNullOrWhiteSpace(effectEvent.StableEventId.Value))
        {
            diagnostic = "Visual effect event ID cannot be empty.";
            return false;
        }
        if (!catalog.TryGet(effectEvent.EffectId, out EffectDefinition? definition))
        {
            diagnostic = $"Unknown visual effect '{effectEvent.EffectId}'.";
            return false;
        }
        if (!float.IsFinite(effectEvent.Scale) || effectEvent.Scale <= 0
            || !float.IsFinite(effectEvent.Intensity) || effectEvent.Intensity < 0)
        {
            diagnostic = "Visual effect scale and intensity must be finite; scale must be positive and intensity non-negative.";
            return false;
        }
        if (effectEvent.Position is Vector2 position
            && (!float.IsFinite(position.X) || !float.IsFinite(position.Y)))
        {
            diagnostic = "Visual effect position must be finite.";
            return false;
        }
        if (effectEvent.Direction is Vector2 direction
            && (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y)))
        {
            diagnostic = "Visual effect direction must be finite.";
            return false;
        }
        if (effectEvent.Space == EffectCoordinateSpace.World && effectEvent.Position is null)
        {
            diagnostic = "World-space visual effects require an explicit position.";
            return false;
        }
        if (definition!.EmitterKind == EffectEmitterKind.ScreenFlash
            && effectEvent.Space != EffectCoordinateSpace.Screen)
        {
            diagnostic = "Screen flash effects require screen coordinate space.";
            return false;
        }
        diagnostic = null;
        return true;
    }

    private void RememberEvent(VisualEffectEventId eventId)
    {
        realizedEvents.Add(eventId);
        realizedOrder.Enqueue(eventId);
        while (realizedOrder.Count > dedupeCapacity)
        {
            realizedEvents.Remove(realizedOrder.Dequeue());
        }
    }

    private static ParticleState Spawn(EmitterState emitter, int ordinal)
    {
        ulong state = Mix(emitter.Event.Seed ^ StableHash(emitter.Event.StableEventId.Value) ^ (ulong)ordinal);
        float angle = NextUnit(ref state) * MathF.Tau;
        float speed = Lerp(emitter.Definition.MinimumSpeed, emitter.Definition.MaximumSpeed, NextUnit(ref state));
        Vector2 direction = emitter.Event.Direction is Vector2 requested && requested.LengthSquared() > 0
            ? Vector2.Normalize(requested)
            : new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        if (emitter.Definition.EmitterKind != EffectEmitterKind.Trail)
        {
            direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }
        float size = Lerp(emitter.Definition.MinimumSize, emitter.Definition.MaximumSize, NextUnit(ref state))
            * emitter.Event.Scale;
        Vector2 origin = emitter.Event.Position ?? Vector2.Zero;
        return new ParticleState(
            emitter.InstanceId,
            emitter.Event.EffectId,
            origin,
            direction * speed * emitter.Event.Scale,
            0,
            (float)emitter.Definition.Lifetime.TotalSeconds,
            size,
            NextUnit(ref state) * MathF.Tau,
            (uint)(state & 3),
            state,
            emitter.Definition.PainterLayer,
            emitter.Definition.BlendMode,
            emitter.Event.Space);
    }

    private static float Lerp(float minimum, float maximum, float amount)
        => minimum + ((maximum - minimum) * amount);

    private static float NextUnit(ref ulong state)
    {
        state = Mix(state);
        return (state >> 40) * (1f / 16_777_216f);
    }

    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    private static ulong StableHash(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (char character in value)
        {
            hash ^= character;
            hash *= prime;
        }
        return hash;
    }

    private sealed class EmitterState(
        EmitterInstanceId instanceId,
        VisualEffectEvent effectEvent,
        EffectDefinition definition)
    {
        public EmitterInstanceId InstanceId { get; } = instanceId;
        public VisualEffectEvent Event { get; } = effectEvent;
        public EffectDefinition Definition { get; } = definition;
        public float AgeSeconds { get; set; }
        public float SpawnDebt { get; set; }
        public int SpawnOrdinal { get; set; }
    }

    private struct ParticleState(
        EmitterInstanceId emitterId,
        VisualEffectId effectId,
        Vector2 position,
        Vector2 velocity,
        float ageSeconds,
        float lifetimeSeconds,
        float size,
        float rotation,
        uint variant,
        ulong seed,
        EffectPainterLayer painterLayer,
        EffectBlendMode blendMode,
        EffectCoordinateSpace space)
    {
        public EmitterInstanceId EmitterId = emitterId;
        public VisualEffectId EffectId = effectId;
        public Vector2 Position = position;
        public Vector2 Velocity = velocity;
        public float AgeSeconds = ageSeconds;
        public float LifetimeSeconds = lifetimeSeconds;
        public float Size = size;
        public float Rotation = rotation;
        public uint Variant = variant;
        public ulong Seed = seed;
        public EffectPainterLayer PainterLayer = painterLayer;
        public EffectBlendMode BlendMode = blendMode;
        public EffectCoordinateSpace Space = space;

        public readonly ParticleSnapshot Snapshot => new(
            EmitterId,
            EffectId,
            Position,
            Velocity,
            AgeSeconds,
            LifetimeSeconds,
            Size,
            Rotation,
            Variant,
            Seed,
            PainterLayer,
            BlendMode,
            Space);
    }
}
