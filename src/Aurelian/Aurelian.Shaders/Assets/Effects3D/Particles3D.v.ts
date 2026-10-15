// Stateless GPU particles. Each slot's position is a pure function of the emitter command and the clock, so replay
// and rewind need no retained simulation. Turbulence is the curl of a small spectral vector potential (curl noise,
// Bridson et al. 2007, with octave amplitudes following Kolmogorov scaling as in Stam and Fiume 1993), integrated over
// the particle's age in a fixed number of steps. The field is divergence-free, so smoke swirls instead of collapsing.
record Emitter {
    originTime: float4;
    directionLife: float4;
    shape: float4;
    color: float4;
    emission: float4;
    glow: float4;
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
    centre: float4;
    detail: float4;
    glow: float4;
}
enum Effect { Fire, Smoke, Sparks, Embers }
record Flight {
    position: float3;
    velocity: float3;
}
function EffectFor(kind: u32): Effect {
    if (kind == 0) {
        return Effect.Fire;
    }
    if (kind == 1) {
        return Effect.Smoke;
    }
    if (kind == 2) {
        return Effect.Sparks;
    }
    return Effect.Embers;
}
function Random(seed: u32): f32 {
    var bits = seed;
    bits = bits * 1664525 + 1013904223;
    bits = (bits % 65536 + bits / 65536) * 1664525 + 1013904223;
    bits = bits * 1664525 + 1013904223;
    return Convert<f32>(bits % 16777216) / 16777216.0;
}
// One Fourier mode of the vector potential: curl(e sin(k.p + w)) = cos(k.p + w) (k x e). Dividing by |k| makes
// the amplitude a velocity in metres per second.
function Mode(p: float3, wave: float3, polarisation: float3, phase: f32, amplitude: f32): float3 {
    let along = Dot(wave, p) + phase;
    let swirl = Cross(wave, polarisation);
    return swirl * (amplitude * Cos(along) / Length(wave));
}
// Four octaves, each about twice the wavenumber of the last; velocity amplitude falls by 2^(-1/3) per octave.
function Turbulence(p: float3, time: f32, scale: f32): float3 {
    let k = 1.0 / scale;
    let a = Mode(p, float3(0.83, 0.31, -0.46) * k, float3(0.12, 0.95, 0.29), time * 0.9, 1.0);
    let b = Mode(p, float3(-0.52, 0.77, 0.88) * (k * 2.1), float3(0.71, -0.22, 0.66), 1.7 + time * 1.7, 0.79);
    let c = Mode(p, float3(1.61, -0.94, 1.12) * (k * 4.3), float3(-0.35, 0.58, 0.74), 4.1 + time * 3.1, 0.62);
    let d = Mode(p, float3(-2.9, 2.2, -2.4) * (k * 8.4), float3(0.83, 0.41, -0.38), 2.3 + time * 5.3, 0.49);
    return (a + b + c + d) * 0.42;
}
// Integrate the base trajectory plus turbulence from birth to the current age.
function Fly(origin: float3, velocity: float3, acceleration: f32, gravity: f32, drag: f32,
    turbulence: f32, growth: f32, scale: f32, born: f32, age: f32, offset: float3): Flight {
    let steps = 8.0;
    let dt = age / steps;
    var position = origin;
    var current = velocity;
    static for (let step of Indices<8>()) {
        let s = (Convert<f32>(step) + 0.5) * dt;
        let lift = float3(0.0, acceleration * Exp(-s * 1.5) - gravity, 0.0);
        let strength = turbulence * (0.35 + growth * s);
        let swirl = Turbulence(position + offset, born + s, scale) * strength;
        current = current + lift * dt;
        current = current * Exp(-drag * dt);
        position = position + (current + swirl) * dt;
    }
    return { position: position, velocity: current + Turbulence(position + offset, born + age, scale) * (turbulence * (0.35 + growth * age)) };
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
    let rate = emitter.shape.x;
    let life = emitter.directionLife.w;
    let period = Convert<f32>(emitter.slots) / rate;
    let delay = Convert<f32>(slot) / rate;
    let lastBirthTime = Min(emitter.originTime.w, emitter.emission.x);
    let cycle = Floor(Max((lastBirthTime - delay) / period, 0.0));
    let birth = delay + cycle * period;
    let age = emitter.originTime.w - birth;
    let phase = Clamp(age / life, 0.0, 1.0);
    let seed = emitter.seed + slot * 747796405 + U32(cycle) * 1013904223;
    let a = Random(seed);
    let b = Random(seed + 1);
    let c = Random(seed + 2);
    let d = Random(seed + 3);
    let e = Random(seed + 4);
    let effect = EffectFor(emitter.kind);
    let direction = emitter.directionLife.xyz;
    let speed = emitter.shape.y;
    let size = emitter.shape.w;
    let turbulence = emitter.emission.z;
    let temperature = emitter.emission.w;

    // Uniform point in a disc of the emitter radius.
    let spread = Sqrt(a) * emitter.shape.z;
    let angle = c * 6.2831853;
    let origin = emitter.originTime.xyz + float3(Cos(angle) * spread, 0.0, Sin(angle) * spread);
    let jitter = float3(b - 0.5, d - 0.5, e - 0.5);
    let offset = float3(Convert<f32>(emitter.seed % 977), Convert<f32>(emitter.seed % 613), Convert<f32>(emitter.seed % 421)) * 0.37;

    var flight: Flight = { position: origin, velocity: direction * speed };
    var radius = size;
    var stretch = 0.0;
    var heat = temperature;
    var opacity = emitter.color.w;
    var glow = float4(0.0, 0.0, 0.0, 0.0);
    if (effect == Effect.Fire) {
        // Hot gas accelerates upward; flames nearer the rim burn shorter and lean inward.
        let rim = spread / Max(emitter.shape.z, 0.0001);
        let inward = float3(-Cos(angle), 0.0, -Sin(angle)) * (rim * 0.25 * speed);
        flight = Fly(origin, direction * (speed * (0.55 + 0.45 * d)) + inward + jitter * (0.25 * speed), speed * 2.2, 0.0, 0.6,
            turbulence, 2.5, size * 3.0, birth, age, offset);
        // Grow quickly from the fuel, then shrink as the flame tongue thins out.
        radius = size * (0.55 + 0.45 * b) * Min(1.0, 0.35 + phase * 4.0) * (1.0 - 0.55 * phase) * (1.0 - 0.35 * rim);
        heat = temperature * (1.0 - 0.42 * phase) * (0.9 + 0.1 * e) * (1.0 - 0.12 * rim);
        opacity = emitter.color.w * Min(phase * 10.0, 1.0) * (1.0 - phase * phase);
    }
    if (effect == Effect.Smoke) {
        flight = Fly(origin, direction * (speed * (0.7 + 0.3 * d)) + jitter * (0.3 * speed), speed * 0.6, 0.0, 0.35,
            turbulence, 0.6, size * 4.0, birth, age, offset);
        // Smoke expands as it entrains air: radius grows roughly with the square root of age.
        radius = size * (0.7 + 0.3 * b) * (0.45 + 2.4 * Pow(phase, 0.65));
        opacity = emitter.color.w * Min(phase * 6.0, 1.0) * (1.0 - phase) * (1.0 - phase) / (0.45 + 2.4 * Pow(phase, 0.65));
        // Light from the fire below, falling off with height above the emitter.
        let height = Max(flight.position.y - emitter.originTime.y, 0.0) / Max(emitter.glow.w, 0.0001);
        let falloff = 1.0 / (1.0 + height * height * 6.0);
        glow = float4(emitter.glow.x * falloff, emitter.glow.y * falloff, emitter.glow.z * falloff, 0.0);
    }
    if (effect == Effect.Sparks) {
        let burst = float3((b - 0.5) * 1.6, 0.4 + d, (e - 0.5) * 1.6) * speed;
        flight = Fly(origin, direction * (speed * 0.4) + burst, 0.0, 9.81, 0.9, turbulence, 0.5, 0.8, birth, age, offset);
        radius = size * (0.6 + 0.6 * a);
        heat = temperature * (1.0 - 0.45 * phase) * (0.85 + 0.15 * d);
        opacity = emitter.color.w * Min(phase * 30.0, 1.0) * (1.0 - phase * phase);
        stretch = 1.0;
    }
    if (effect == Effect.Embers) {
        // Light flakes: lifted by the plume, carried by the swirl, slowly cooling and winking out.
        flight = Fly(origin, direction * (speed * (0.5 + 0.8 * d)) + jitter * (0.8 * speed), speed * 1.4, 0.35, 0.8,
            turbulence, 1.2, 0.9, birth, age, offset);
        radius = size * (0.5 + 0.8 * a * a);
        heat = temperature * (1.0 - 0.5 * phase) * (0.8 + 0.2 * e);
        let twinkle = 0.75 + 0.25 * Sin(age * (9.0 + 14.0 * b) + c * 6.2831853);
        opacity = emitter.color.w * Min(phase * 15.0, 1.0) * (1.0 - phase) * twinkle;
        stretch = 1.0;
    }

    var centre = flight.position;
    if (age < 0.0 || age >= life || birth > emitter.emission.x || slot >= emitter.slots) {
        centre = emitter.originTime.xyz;
        opacity = 0.0;
        radius = 0.0;
        stretch = 0.0;
    }

    // Billboard axes in the camera plane. Flames lean along their velocity; sparks and embers stretch along it,
    // covering the distance travelled during a 1/60 s shutter (motion blur that conserves the particle's energy).
    let right = camera.right.xyz;
    let up = camera.up.xyz;
    let screen = float2(Dot(flight.velocity, right), Dot(flight.velocity, up));
    let screenSpeed = Length(screen);
    var axisAlong = up;
    var axisAcross = right;
    var halfAlong = radius;
    var halfAcross = radius;
    var centreOffset = float3(0.0, 0.0, 0.0);
    if (effect == Effect.Fire) {
        halfAlong = radius * 1.7;
        if (screenSpeed > 0.05) {
            let lean = float2(screen.x / screenSpeed * 0.6, 0.4 + Max(screen.y / screenSpeed, 0.0) * 0.6);
            let leanLength = Length(lean);
            axisAlong = right * (lean.x / leanLength) + up * (lean.y / leanLength);
            axisAcross = right * (lean.y / leanLength) - up * (lean.x / leanLength);
        }
        // Anchor the flame near its base so tongues rise from the fuel instead of being centred on it.
        centreOffset = axisAlong * (halfAlong * 0.45);
    }
    if (effect == Effect.Smoke) {
        // Cover the projected sphere generously; the pixel shader integrates the sphere itself.
        halfAlong = radius * 1.15;
        halfAcross = radius * 1.15;
    }
    if (stretch > 0.5 && screenSpeed > 0.01) {
        let trail = screenSpeed * (1.0 / 60.0) * 1.5;
        axisAlong = right * (screen.x / screenSpeed) + up * (screen.y / screenSpeed);
        axisAcross = right * (screen.y / screenSpeed) - up * (screen.x / screenSpeed);
        halfAlong = radius + trail * 0.5;
        centreOffset = axisAlong * (-trail * 0.5);
        // Spread the same energy over the longer streak.
        opacity = opacity * radius / halfAlong;
    }
    let quad = centre + centreOffset;
    let corners: Array<float2, 6> = [float2(-1.0, -1.0), float2(1.0, -1.0), float2(1.0, 1.0),
        float2(-1.0, -1.0), float2(1.0, 1.0), float2(-1.0, 1.0)];
    static for (let index of Indices<6>()) {
        let uv = corners[index];
        let position = quad + axisAcross * (uv.x * halfAcross) + axisAlong * (uv.y * halfAlong);
        Vertices[thread.x * 6 + index] = {
            position: position,
            uvKind: float4(uv.x, uv.y, Convert<f32>(emitter.kind), emitter.emission.y),
            color: float4(emitter.color.x, emitter.color.y, emitter.color.z, opacity),
            centre: float4(centre.x, centre.y, centre.z, radius),
            detail: float4(a * 97.0 + b * 13.0, age, heat, phase),
            glow: glow,
        };
    }
}
