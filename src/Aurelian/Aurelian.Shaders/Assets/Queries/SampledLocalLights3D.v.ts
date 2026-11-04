import { AffineAnalyticIntersection } from "./AffineAnalyticQuery3D";
import { WorldAt, LocalAttenuation } from "./SurfaceLighting";
import { Dot3, Unit } from "./Lighting3D";
import { Read4, HalfToFloat, LightRandom } from "./LocalLightSamplingMath3D";

function MinIndex(first: u32, second: u32): u32 {
    if (first < second) { return first; }
    return second;
}
function RawTarget(readonly parameters: StorageBuffer<f32>, point: float3, normal: float3, light: u32): f32 {
    const offset: u32 = 64 + light * 16;
    const sphere: float4 = Read4(parameters, offset);
    const color: float4 = Read4(parameters, offset + 4);
    const delta: float3 = float3(sphere.x, sphere.y, sphere.z) - point;
    const direction: float3 = Unit(delta);
    const attenuation: f32 = LocalAttenuation(direction, Dot3(delta, delta), sphere.w,
        Read4(parameters, offset + 8), Read4(parameters, offset + 12));
    const power: f32 = color.w * (color.x * 0.2126 + color.y * 0.7152 + color.z * 0.0722);
    return Min(power * attenuation * Max(Dot3(normal, direction), 0.0), 1.0e20);
}
function Target(readonly parameters: StorageBuffer<f32>, point: float3, normal: float3, light: u32): f32 {
    // Positive support keeps temporal normalization well-defined for dark candidates.
    return Max(0.00000001, RawTarget(parameters, point, normal, light));
}
function Proposal(readonly parameters: StorageBuffer<f32>, point: float3, normal: float3, light: u32, mode: u32): f32 {
    if (mode == 6) {
        if (RawTarget(parameters, point, normal, light) > 0.0) { return 1.0; }
        return 0.0;
    }
    return Target(parameters, point, normal, light);
}
function IsExact(light: u32, top: float3, count: u32): bool {
    return (count > 0 && Convert<f32>(light) == top.x) || (count > 1 && Convert<f32>(light) == top.y) || (count > 2 && Convert<f32>(light) == top.z);
}

@compute
@numthreads(64, 1, 1)
function LocalShadowMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Scene: acceleration_structure,
    @binding(1) readonly Procedurals: StorageBuffer<f32>,
    @binding(2) readwrite Rays: StorageBuffer<f32>,
    @binding(3) readwrite Hits: StorageBuffer<f32>,
    @binding(4) readonly Instances: StorageBuffer<f32>,
    @binding(5) readonly Parameters: StorageBuffer<f32>,
    @binding(6) readonly Motion: StorageBuffer<f32>,
    @binding(7) readwrite Visibility: StorageBuffer<f32>,
    @binding(8) readonly Normals: StorageBuffer<u32>,
    @binding(9) readonly History: StorageBuffer<f32>,
    @binding(10) readwrite NextHistory: StorageBuffer<f32>): void {
    const pixel: u32 = thread.x;
    const width: u32 = U32(Parameters[16]);
    const height: u32 = U32(Parameters[17]);
    const pixels: u32 = width * height;
    if (pixel >= pixels) { return; }
    const mode: u32 = U32(Parameters[23]);
    const count: u32 = U32(Parameters[18]);
    const frame: u32 = Bitcast<u32>(Parameters[28]);
    const seed: u32 = Bitcast<u32>(Parameters[35]);
    const rank: u32 = (pixel + frame * 997) % pixels;
    const budget: u32 = U32(Parameters[24]);
    var quota: u32 = budget / pixels;
    if (rank < budget % pixels) { quota = quota + 1; }
    const uv: float2 = float2((Convert<f32>(pixel % width) + 0.5) / Parameters[16],
        (Convert<f32>(pixel / width) + 0.5) / Parameters[17]);
    const depth: f32 = Motion[pixel * 4 + 3];
    const point: float3 = WorldAt(uv, depth, Read4(Parameters, 0), Read4(Parameters, 4), Read4(Parameters, 8), Read4(Parameters, 12));
    const xy: u32 = Normals[pixel * 2];
    const zw: u32 = Normals[pixel * 2 + 1];
    const normal: float3 = float3(HalfToFloat(xy & 65535), HalfToFloat(xy >> 16), HalfToFloat(zw & 65535));
    const origin: float3 = point + normal * Parameters[22];
    const packet: u32 = 4 + pixel * 12;
    Rays[packet] = origin.x;
    Rays[packet + 1] = origin.y;
    Rays[packet + 2] = origin.z;
    Rays[packet + 3] = Parameters[19];
    Rays[packet + 8] = Parameters[20];
    Rays[packet + 9] = Parameters[21];
    const historyBase: u32 = pixel * 24;
    var historyValid: bool = false;
    // Keep the resource read behind control flow. Legacy HLSL boolean operators
    // may evaluate both operands; non-temporal modes bind only a small placeholder.
    if (mode == 5 && Parameters[29] > 0.5 && depth < 1.0) {
        if (History[historyBase + 23] > 0.5) {
            const difference: float3 = point - float3(History[historyBase + 16], History[historyBase + 17], History[historyBase + 18]);
            const oldNormal: float3 = float3(History[historyBase + 20], History[historyBase + 21], History[historyBase + 22]);
            historyValid = Dot3(difference, difference) < 0.000001 && Dot3(normal, oldNormal) > 0.999;
        }
    }
    for (let word of BoundedIndices<16>(16)) { Visibility[pixel * 16 + word] = 0.0; }
    if (mode == 5) {
        for (let word of BoundedIndices<24>(24)) { NextHistory[historyBase + word] = 0.0; }
        NextHistory[historyBase + 16] = point.x;
        NextHistory[historyBase + 17] = point.y;
        NextHistory[historyBase + 18] = point.z;
        NextHistory[historyBase + 20] = normal.x;
        NextHistory[historyBase + 21] = normal.y;
        NextHistory[historyBase + 22] = normal.z;
        if (depth < 1.0) { NextHistory[historyBase + 23] = 1.0; }
    }
    var raysUsed: u32 = 0;
    var reused: u32 = 0;
    var exactCount: u32 = 0;
    var top: float3 = float3(-1.0, -1.0, -1.0);
    var topWeights: float3 = float3(0.0, 0.0, 0.0);
    var total: f32 = 0.0;
    if (depth < 1.0 && (mode == 2 || mode == 3 || mode == 6)) {
        for (let light of BoundedIndices<256>(256)) {
            if (light < count) {
                const target: f32 = Proposal(Parameters, point, normal, light, mode);
                total = total + target;
                if (target > topWeights.x) {
                    top = float3(Convert<f32>(light), top.x, top.y);
                    topWeights = float3(target, topWeights.x, topWeights.y);
                } else {
                    if (target > topWeights.y) {
                        top = float3(top.x, Convert<f32>(light), top.y);
                        topWeights = float3(topWeights.x, target, topWeights.y);
                    } else {
                        if (target > topWeights.z) {
                            top = float3(top.x, top.y, Convert<f32>(light));
                            topWeights = float3(topWeights.x, topWeights.y, target);
                        }
                    }
                }
            }
        }
        if (mode == 3) {
            exactCount = MinIndex(U32(Parameters[25]), MinIndex(quota - 1, count));
            // Sum the tail directly to avoid catastrophic cancellation around a dominant key light.
            total = 0.0;
            for (let light of BoundedIndices<256>(256)) {
                if (light < count && !IsExact(light, top, exactCount)) { total = total + Target(Parameters, point, normal, light); }
            }
        }
    }
    // Four inverse-CDF draws share one traversal of the light table.
    var choices: float4 = float4(-1.0, -1.0, -1.0, -1.0);
    if (depth < 1.0 && (mode == 2 || mode == 3 || mode == 6)) {
        const baseKey: u32 = pixel * 0x9e3779b9 + frame * 0x85ebca6b + seed;
        const thresholds: float4 = float4(LightRandom(baseKey) * total,
            LightRandom(baseKey + 0xc2b2ae35) * total,
            LightRandom(baseKey + 0x85655c6a) * total,
            LightRandom(baseKey + 0x48180a9f) * total);
        var cumulative: f32 = 0.0;
        for (let light of BoundedIndices<256>(256)) {
            if (light < count && !IsExact(light, top, exactCount)) {
                cumulative = cumulative + Proposal(Parameters, point, normal, light, mode);
                if (choices.x < 0.0 && cumulative >= thresholds.x) { choices = float4(Convert<f32>(light), choices.y, choices.z, choices.w); }
                if (choices.y < 0.0 && cumulative >= thresholds.y) { choices = float4(choices.x, Convert<f32>(light), choices.z, choices.w); }
                if (choices.z < 0.0 && cumulative >= thresholds.z) { choices = float4(choices.x, choices.y, Convert<f32>(light), choices.w); }
                if (choices.w < 0.0 && cumulative >= thresholds.w) { choices = float4(choices.x, choices.y, choices.z, Convert<f32>(light)); }
            }
        }
    }
    for (let sample of BoundedIndices<4>(4)) {
        if (sample < quota && depth < 1.0 && count > 0 && (sample < exactCount || count > exactCount) && (mode != 6 || total > 0.0)) {
            const key: u32 = pixel * 0x9e3779b9 + frame * 0x85ebca6b + seed + sample * 0xc2b2ae35;
            var selected: u32 = MinIndex(U32(LightRandom(key) * Convert<f32>(count)), count - 1);
            var weight: f32 = Convert<f32>(count) / Convert<f32>(quota);
            var reservoirWeight: f32 = 0.0;
            var candidates: f32 = 0.0;
            if (sample < exactCount) {
                selected = U32(top.x);
                if (sample == 1) { selected = U32(top.y); }
                if (sample == 2) { selected = U32(top.z); }
                weight = 1.0;
            } else {
                if (mode == 2 || mode == 3 || mode == 6) {
                    selected = U32(choices.x);
                    if (sample == 1) { selected = U32(choices.y); }
                    if (sample == 2) { selected = U32(choices.z); }
                    if (sample == 3) { selected = U32(choices.w); }
                    weight = total / (Convert<f32>(quota - exactCount) * Proposal(Parameters, point, normal, selected, mode));
                }
                if (mode == 4 || mode == 5) {
                    var sum: f32 = 0.0;
                    for (let candidate of BoundedIndices<32>(32)) {
                        if (candidate < U32(Parameters[26])) {
                            const item: u32 = MinIndex(U32(LightRandom(key + candidate * 17 + 1) * Convert<f32>(count)), count - 1);
                            const value: f32 = Target(Parameters, point, normal, item) * Convert<f32>(count);
                            sum = sum + value;
                            if (LightRandom(key + candidate * 17 + 2) * sum < value) { selected = item; }
                            candidates = candidates + 1.0;
                        }
                    }
                    if (historyValid) {
                        if (History[historyBase + sample * 4 + 2] > 0.0) {
                            const oldLight: u32 = U32(History[historyBase + sample * 4]);
                            const oldCount: f32 = Min(History[historyBase + sample * 4 + 2], Parameters[27]);
                            const value: f32 = Target(Parameters, point, normal, oldLight) * History[historyBase + sample * 4 + 1] * oldCount;
                            sum = sum + value;
                            if (LightRandom(key + 0x51ed270b) * sum < value) { selected = oldLight; }
                            candidates = candidates + oldCount;
                            reused = reused + 1;
                        }
                    }
                    reservoirWeight = sum / (candidates * Proposal(Parameters, point, normal, selected, mode));
                    weight = reservoirWeight / Convert<f32>(quota);
                }
            }
            if (mode == 5) {
                NextHistory[historyBase + sample * 4] = Convert<f32>(selected);
                NextHistory[historyBase + sample * 4 + 1] = reservoirWeight;
                NextHistory[historyBase + sample * 4 + 2] = candidates;
            }
            const offset: u32 = 64 + selected * 16;
            const sphere: float4 = Read4(Parameters, offset);
            const delta: float3 = float3(sphere.x, sphere.y, sphere.z) - origin;
            const distance: f32 = Sqrt(Dot3(delta, delta));
            if (Parameters[offset + 14] > 0.5 && distance > Parameters[19] * 2.0
                && distance < sphere.w && Dot3(normal, delta) > 0.0) {
                const direction: float3 = delta / distance;
                Rays[packet + 4] = direction.x;
                Rays[packet + 5] = direction.y;
                Rays[packet + 6] = direction.z;
                Rays[packet + 7] = distance - Parameters[19];
                RayQueryTraceOccluded(Scene, Procedurals, Rays, Hits, Instances, pixel, AffineAnalyticIntersection);
                raysUsed = raysUsed + 1;
                if (Hits[pixel] > 0.5) { weight = 0.0; }
            }
            Visibility[pixel * 16 + sample * 2] = Convert<f32>(selected);
            Visibility[pixel * 16 + sample * 2 + 1] = weight;
        }
    }
    Visibility[pixel * 16 + 8] = Convert<f32>(raysUsed);
    Visibility[pixel * 16 + 9] = Convert<f32>(quota);
    Visibility[pixel * 16 + 10] = Convert<f32>(reused);
}
