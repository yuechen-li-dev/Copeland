import { Add3, Sub3, Scale3, Cross3, Unit } from "./Lighting3D";

export record WaterDifferential {
    offset: float3;
    dx: float3;
    dz: float3;
}

export record OceanCell {
    westSouth: float4;
    eastSouth: float4;
    westNorth: float4;
    eastNorth: float4;
    blend: float2;
}

// The upper and lower halves of the atlas are independent periodic fields.
// Wrap each neighbor within its own time plane.
export function OceanCellAt(atlas: Texture2D<float4>, p: float3,
    tileLength: f32, plane: u32): OceanCell {
    let x: f32 = p.x / tileLength;
    let z: f32 = p.z / tileLength;
    let cellX: f32 = (x - Floor(x)) * 64.0;
    let cellZ: f32 = (z - Floor(z)) * 64.0;
    let firstX: f32 = Floor(cellX);
    let firstZ: f32 = Floor(cellZ);
    let u0: u32 = Convert<u32>(firstX);
    let v0: u32 = Convert<u32>(firstZ);
    let u1: u32 = (u0 + 1) & 63;
    let v1: u32 = (v0 + 1) & 63;
    return {
        westSouth: Load(atlas, uint2(u0, plane + v0)),
        eastSouth: Load(atlas, uint2(u1, plane + v0)),
        westNorth: Load(atlas, uint2(u0, plane + v1)),
        eastNorth: Load(atlas, uint2(u1, plane + v1)),
        blend: float2(cellX - firstX, cellZ - firstZ),
    };
}

function OceanMix(a: float4, b: float4, t: f32): float4 {
    return float4(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t,
        a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t);
}

export function OceanSample(cell: OceanCell): float4 {
    let south: float4 = OceanMix(cell.westSouth, cell.eastSouth, cell.blend.x);
    let north: float4 = OceanMix(cell.westNorth, cell.eastNorth, cell.blend.x);
    return OceanMix(south, north, cell.blend.y);
}

export function OceanDifferential(cell: OceanCell, tileLength: f32): WaterDifferential {
    let offset: float4 = OceanSample(cell);
    let scale: f32 = 64.0 / tileLength;
    let sw: float3 = OceanOffset(cell.westSouth);
    let se: float3 = OceanOffset(cell.eastSouth);
    let nw: float3 = OceanOffset(cell.westNorth);
    let ne: float3 = OceanOffset(cell.eastNorth);
    let dx: float3 = Add3(Scale3(Sub3(se, sw), 1.0 - cell.blend.y), Scale3(Sub3(ne, nw), cell.blend.y));
    let dz: float3 = Add3(Scale3(Sub3(nw, sw), 1.0 - cell.blend.x), Scale3(Sub3(ne, se), cell.blend.x));
    return { offset: OceanOffset(offset), dx: Scale3(dx, scale), dz: Scale3(dz, scale) };
}

function OceanOffset(sample: float4): float3 {
    return float3(sample.x, sample.y, sample.z);
}

function OceanHash(x: f32, z: f32): f32 {
    let value: f32 = Sin(x * 127.1 + z * 311.7) * 43758.5453;
    return value - Floor(value);
}

// Value noise and its analytic gradient, with continuous slopes at cell edges.
function OceanNoise(point: float2): float3 {
    let x: f32 = Floor(point.x);
    let z: f32 = Floor(point.y);
    let tx: f32 = point.x - x;
    let tz: f32 = point.y - z;
    let wx: f32 = tx * tx * (3.0 - 2.0 * tx);
    let wz: f32 = tz * tz * (3.0 - 2.0 * tz);
    let sw: f32 = OceanHash(x, z);
    let se: f32 = OceanHash(x + 1.0, z);
    let nw: f32 = OceanHash(x, z + 1.0);
    let ne: f32 = OceanHash(x + 1.0, z + 1.0);
    let south: f32 = sw + (se - sw) * wx;
    let north: f32 = nw + (ne - nw) * wx;
    let dx: f32 = ((se - sw) * (1.0 - wz) + (ne - nw) * wz) * 6.0 * tx * (1.0 - tx);
    let dz: f32 = (north - south) * 6.0 * tz * (1.0 - tz);
    return float3(south + (north - south) * wz, dx, dz);
}

export function OceanDetailNormal(normal: float3, p: float3, time: f32,
    amplitude: f32, wavelength: f32, footprint: f32): float3 {
    let weight: f32 = Clamp((0.65 - footprint / wavelength) / 0.4, 0.0, 1.0);
    let scale: f32 = 2.0 * amplitude * weight / wavelength;
    if (scale <= 0.0) {
        return normal;
    }
    let detail: float3 = OceanNoise(float2(p.x / wavelength - time * 0.53, p.z / wavelength - time * 0.29));
    return Unit(Add3(normal, float3(-detail.y * scale, 0.0, -detail.z * scale)));
}

export function OceanFoamGrain(p: float3, footprint: f32): f32 {
    let weight: f32 = Clamp((0.11 - footprint) / 0.07, 0.0, 1.0);
    if (weight <= 0.0) {
        return 0.81;
    }
    let grain: float3 = OceanNoise(float2(p.x * 6.0 + p.z * 1.3, p.z * 6.0 - p.x * 1.3));
    return 0.81 + (grain.x - 0.5) * 0.38 * weight;
}

export function WindWave(p: float3, time: f32, wave: float4, behavior: float4): WaterDifferential {
    let k: f32 = 6.2831853 / Max(wave.w, 0.1);
    let phase: f32 = k * (wave.x * p.x + wave.y * p.z - behavior.x * time);
    let sine: f32 = Sin(phase);
    let cosine: f32 = Cos(phase);
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
    let age: f32 = time - event.z;
    var height: f32 = 0.0;
    var gradient: float3 = float3(0.0, 0.0, 0.0);
    if (behavior.z > 0.0 && age > 0.0 && age < behavior.z) {
        let x: f32 = p.x - event.x;
        let z: f32 = p.z - event.y;
        let radius: f32 = Sqrt(x * x + z * z + 0.000001);
        let k: f32 = 6.2831853 / behavior.x;
        let delta: f32 = radius - behavior.y * age;
        let band: f32 = behavior.x * 0.8;
        let life: f32 = 1.0 - age / behavior.z;
        let birth: f32 = Clamp(age * 12.0, 0.0, 1.0);
        let envelope: f32 = Exp(-delta * delta / (2.0 * band * band)) * life * life * birth;
        let phase: f32 = k * delta;
        height = event.w * Sin(phase) * envelope;
        let derivative: f32 = event.w * envelope * (k * Cos(phase) - delta * Sin(phase) / (band * band));
        gradient = float3(derivative * x / radius, 0.0, derivative * z / radius);
    }
    return { offset: float3(0.0, height, 0.0), dx: float3(0.0, gradient.x, 0.0), dz: float3(0.0, gradient.z, 0.0) };
}

export function WaterNormal(differential: WaterDifferential): float3 {
    return Unit(Cross3(Add3(float3(0.0, 0.0, 1.0), differential.dz),
        Add3(float3(1.0, 0.0, 0.0), differential.dx)));
}

export function DetailNormal(normal: float3, p: float3, time: f32, amplitude: f32, wavelength: f32): float3 {
    let k: f32 = 6.2831853 / wavelength;
    let a: f32 = Cos(k * (p.x * 0.8 + p.z * 0.6 - time * 0.23)) * k * amplitude;
    let b: f32 = Cos(k * 1.37 * (p.x * -0.6 + p.z * 0.8 - time * 0.17)) * k * amplitude * 0.5;
    return Unit(Add3(normal, float3(-0.8 * a + 0.6 * b, 0.0, -0.6 * a - 0.8 * b)));
}
