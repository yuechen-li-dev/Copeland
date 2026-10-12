import { Add3, Scale3, Cross3, Unit } from "./Lighting3D";

export record WaterDifferential {
    offset: float3;
    dx: float3;
    dz: float3;
}

export function WindWave(p: float3, time: f32, wave: float4, behavior: float4): WaterDifferential {
    const k: f32 = 6.2831853 / Max(wave.w, 0.1);
    const phase: f32 = k * (wave.x * p.x + wave.y * p.z - behavior.x * time);
    const sine: f32 = Sin(phase);
    const cosine: f32 = Cos(phase);
    var lateral: f32 = 0.0;
    if (wave.z > 0.0) {
        lateral = behavior.y / k;
    }
    return {
        offset: float3(lateral * wave.x * cosine, wave.z * sine, lateral * wave.y * cosine),
        dx: float3(-lateral * k * wave.x * wave.x * sine, wave.z * k * wave.x * cosine,
            -lateral * k * wave.x * wave.y * sine),
        dz: float3(-lateral * k * wave.x * wave.y * sine, wave.z * k * wave.y * cosine,
            -lateral * k * wave.y * wave.y * sine),
    };
}

export function AddWave(a: WaterDifferential, b: WaterDifferential): WaterDifferential {
    return { offset: Add3(a.offset, b.offset), dx: Add3(a.dx, b.dx), dz: Add3(a.dz, b.dz) };
}

export function ImpactWave(p: float3, time: f32, event: float4, behavior: float4): WaterDifferential {
    const age: f32 = time - event.z;
    var height: f32 = 0.0;
    var gradient: float3 = float3(0.0, 0.0, 0.0);
    if (behavior.z > 0.0 && age > 0.0 && age < behavior.z) {
        const x: f32 = p.x - event.x;
        const z: f32 = p.z - event.y;
        const radius: f32 = Sqrt(x * x + z * z + 0.000001);
        const k: f32 = 6.2831853 / behavior.x;
        const delta: f32 = radius - behavior.y * age;
        const band: f32 = behavior.x * 0.8;
        const life: f32 = 1.0 - age / behavior.z;
        const birth: f32 = Clamp(age * 12.0, 0.0, 1.0);
        const envelope: f32 = Exp(-delta * delta / (2.0 * band * band)) * life * life * birth;
        const phase: f32 = k * delta;
        height = event.w * Sin(phase) * envelope;
        const derivative: f32 = event.w * envelope * (k * Cos(phase) - delta * Sin(phase) / (band * band));
        gradient = float3(derivative * x / radius, 0.0, derivative * z / radius);
    }
    return { offset: float3(0.0, height, 0.0), dx: float3(0.0, gradient.x, 0.0), dz: float3(0.0, gradient.z, 0.0) };
}

export function WaterNormal(differential: WaterDifferential): float3 {
    return Unit(Cross3(Add3(float3(0.0, 0.0, 1.0), differential.dz),
        Add3(float3(1.0, 0.0, 0.0), differential.dx)));
}

export function DetailNormal(normal: float3, p: float3, time: f32, amplitude: f32, wavelength: f32): float3 {
    const k: f32 = 6.2831853 / wavelength;
    const a: f32 = Cos(k * (p.x * 0.8 + p.z * 0.6 - time * 0.23)) * k * amplitude;
    const b: f32 = Cos(k * 1.37 * (p.x * -0.6 + p.z * 0.8 - time * 0.17)) * k * amplitude * 0.5;
    return Unit(Add3(normal, float3(-0.8 * a + 0.6 * b, 0.0, -0.6 * a - 0.8 * b)));
}
