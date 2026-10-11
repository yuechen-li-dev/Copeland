record Emitter {
    originTime: float4;
    directionLife: float4;
    shape: float4;
    color: float4;
    emission: float4;
    seed: u32;
    kind: u32;
    slots: u32;
    reserved: u32;
}
record Camera {
    right: float4;
    up: float4;
}
record ParticleVertex {
    position: float3;
    uvKind: float4;
    color: float4;
}
enum Effect { Fire, Smoke, Sparks }
record Motion {
    velocity: float3;
    acceleration: float3;
    size: f32;
    color: float4;
}
function EffectFor(kind: u32): Effect {
    if (kind == 0) {
        return Effect.Fire;
    }
    if (kind == 1) {
        return Effect.Smoke;
    }
    return Effect.Sparks;
}
function MotionFor(emitter: Emitter, random: float3, phase: f32): Motion {
    let velocity = emitter.directionLife.xyz * emitter.shape.y;
    let color = emitter.color;
    return match EffectFor(emitter.kind) {
        case Effect.Fire => MotionValue(velocity + float3((random.x - 0.5) * 0.6, random.y * 0.7, (random.z - 0.5) * 0.6),
            float3(0.0, 0.7, 0.0), emitter.shape.w * (1.0 - phase * 0.7),
            float4(color.x * (1.0 - phase) * (1.0 - phase), color.y * (1.0 - phase) * (1.0 - phase),
                color.z * (1.0 - phase), color.w)),
        case Effect.Smoke => MotionValue(velocity + float3((random.x - 0.5) * 0.5, random.y * 0.3, (random.z - 0.5) * 0.5),
            float3(0.15, 0.1, 0.0), emitter.shape.w * (0.5 + phase * 2.0), color),
        case Effect.Sparks => MotionValue(velocity + float3((random.x - 0.5) * emitter.shape.y, random.y * emitter.shape.y, (random.z - 0.5) * emitter.shape.y),
            float3(0.0, -9.81, 0.0), emitter.shape.w, color),
    };
}
function MotionValue(velocity: float3, acceleration: float3, size: f32, color: float4): Motion {
    return { velocity: velocity, acceleration: acceleration, size: size, color: color };
}
function Random(seed: u32): f32 {
    var bits = seed;
    bits = bits * 1664525 + 1013904223;
    bits = (bits % 65536 + bits / 65536) * 1664525 + 1013904223;
    bits = bits * 1664525 + 1013904223;
    return Convert<f32>(bits % 16777216) / 16777216.0;
}
@compute
@numthreads(64, 1, 1)
function ParticleMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Emitters: StorageBuffer<Emitter>,
    @binding(1) readonly View: StorageBuffer<Camera>,
    @binding(2) readwrite Vertices: StorageBuffer<ParticleVertex>): void {
    let camera = View[0];
    if (thread.x >= U32(camera.right.w)) {
        return;
    }
    let slots = U32(camera.up.w);
    let emitter = Emitters[thread.x / slots];
    let slot = thread.x % slots;
    let period = Convert<f32>(emitter.slots) / emitter.shape.x;
    let delay = Convert<f32>(slot) / emitter.shape.x;
    let lastBirthTime = Min(emitter.originTime.w, emitter.emission.x);
    let cycle = Floor(Max((lastBirthTime - delay) / period, 0.0));
    let birth = delay + cycle * period;
    let age = emitter.originTime.w - birth;
    let phase = Clamp(age / emitter.directionLife.w, 0.0, 1.0);
    let seed = emitter.seed + slot * 747796405 + U32(cycle) * 1013904223;
    let a = Random(seed);
    let b = Random(seed + 1);
    let c = Random(seed + 2);
    let life = emitter.directionLife.w;
    let motion = MotionFor(emitter, float3(a, b, c), phase);
    var size = motion.size;
    let color = motion.color;
    let origin = emitter.originTime.xyz + float3((a - 0.5) * emitter.shape.z, 0.0, (c - 0.5) * emitter.shape.z);
    var center = origin + motion.velocity * age + motion.acceleration * (0.5 * age * age);
    var opacity = color.w * Min(phase * 12.0, 1.0) * (1.0 - phase) * (1.0 - phase);
    if (age < 0.0 || age >= life || birth > emitter.emission.x || slot >= emitter.slots) {
        center = emitter.originTime.xyz;
        opacity = 0.0;
        size = 0.0;
    }
    let corners: Array<float2, 6> = [float2(-1.0, -1.0), float2(1.0, -1.0), float2(1.0, 1.0),
        float2(-1.0, -1.0), float2(1.0, 1.0), float2(-1.0, 1.0)];
    var height = size;
    if (emitter.kind == 0) { height = size * 1.8; }
    if (emitter.kind == 2) { height = size * 2.5; }
    static for (let index of Indices<6>()) {
        let uv = corners[index];
        let position = center + camera.right.xyz * (uv.x * size) + camera.up.xyz * (uv.y * height);
        Vertices[thread.x * 6 + index] = {
            position: position,
            uvKind: float4(uv.x, uv.y, Convert<f32>(emitter.kind), emitter.emission.y),
            color: float4(color.x, color.y, color.z, opacity),
        };
    }
}
