import { EncodeOcta } from "./SurfaceLighting";
import { Add3, Sub3, Scale3, Dot3 } from "./Lighting3D";

function ProbeBand(map: Texture2D<float4>, sampler: Sampler, index: f32, count: f32, direction: float3): float4 {
    const uv: float2 = EncodeOcta(direction);
    return Sample(map, sampler, float2((uv.x * 7.0 + 0.5) / 8.0,
        (index * 8.0 + uv.y * 7.0 + 0.5) / (count * 8.0)));
}

// Returns E/pi and a validity bit. Distance moments attenuate probes behind geometry.
// This is a low-frequency single-bounce approximation, not exact visibility.
export function ProbeDiffuse(map: Texture2D<float4>, visibility: Texture2D<float4>, sampler: Sampler,
    point: float3, normal: float3, minimum: float4, maximum: float4, size: float4,
    generation: f32, useVisibility: bool): float4 {
    return ProbeDiffuseConnected(map, visibility, sampler, point, normal, minimum, maximum, size,
        generation, useVisibility, 255.0);
}

// An authored site's eight probe connections can be compiled once for static geometry.
// The caller owns invalidation of this mask when the scene or receiver changes.
export function ProbeDiffuseConnected(map: Texture2D<float4>, visibility: Texture2D<float4>, sampler: Sampler,
    point: float3, normal: float3, minimum: float4, maximum: float4, size: float4,
    generation: f32, useVisibility: bool, connections: f32): float4 {
    const spacing: float3 = float3((maximum.x - minimum.x) / (size.x - 1.0),
        (maximum.y - minimum.y) / (size.y - 1.0), (maximum.z - minimum.z) / (size.z - 1.0));
    if (point.x < minimum.x - spacing.x || point.x > maximum.x + spacing.x
        || point.y < minimum.y - spacing.y || point.y > maximum.y + spacing.y
        || point.z < minimum.z - spacing.z || point.z > maximum.z + spacing.z) {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    const biased: float3 = Add3(point, Scale3(normal, 0.03));
    const coordinate: float3 = float3(Clamp((biased.x - minimum.x) / spacing.x, 0.0, size.x - 1.0001),
        Clamp((biased.y - minimum.y) / spacing.y, 0.0, size.y - 1.0001),
        Clamp((biased.z - minimum.z) / spacing.z, 0.0, size.z - 1.0001));
    const cell: float3 = float3(Floor(coordinate.x), Floor(coordinate.y), Floor(coordinate.z));
    const fraction: float3 = Sub3(coordinate, cell);
    var sum: float3 = float3(0.0, 0.0, 0.0);
    var total: f32 = 0.0;
    for (var corner: u32 = 0; corner < 8; corner = corner + 1) {
        const value: f32 = Convert<f32>(corner);
        const shifted: f32 = Floor(connections / Pow(2.0, value));
        const allowed: f32 = shifted - Floor(shifted / 2.0) * 2.0;
        const half: f32 = Floor(value / 2.0);
        const x: f32 = value - half * 2.0;
        const y: f32 = half - Floor(half / 2.0) * 2.0;
        const z: f32 = Floor(value / 4.0);
        const index: f32 = cell.x + x + (cell.y + y) * size.x + (cell.z + z) * size.x * size.y;
        const probe: float3 = float3(minimum.x + (cell.x + x) * spacing.x,
            minimum.y + (cell.y + y) * spacing.y, minimum.z + (cell.z + z) * spacing.z);
        const delta: float3 = Sub3(biased, probe);
        const distance: f32 = Sqrt(Max(Dot3(delta, delta), 0.000001));
        const direction: float3 = Scale3(delta, 1.0 / distance);
        const irradiance: float4 = ProbeBand(map, sampler, index, size.x * size.y * size.z, normal);
        const moments: float4 = ProbeBand(visibility, sampler, index, size.x * size.y * size.z, direction);
        var weight: f32 = (x * fraction.x + (1.0 - x) * (1.0 - fraction.x))
            * (y * fraction.y + (1.0 - y) * (1.0 - fraction.y))
            * (z * fraction.z + (1.0 - z) * (1.0 - fraction.z));
        weight = weight * allowed;
        weight = weight * (0.2 + 0.8 * Max(Dot3(normal, Scale3(direction, -1.0)), 0.0));
        if (useVisibility && distance > moments.x) {
            const variance: f32 = Max(moments.y - moments.x * moments.x, 0.0001);
            const error: f32 = distance - moments.x;
            const probability: f32 = variance / (variance + error * error);
            weight = weight * probability * probability * probability;
        }
        if (irradiance.w == generation && moments.w == generation) {
            sum = Add3(sum, Scale3(float3(irradiance.x, irradiance.y, irradiance.z), weight));
            total = total + weight;
        }
    }
    if (total < 0.00001) {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    sum = Scale3(sum, 1.0 / total);
    return float4(sum.x, sum.y, sum.z, 1.0);
}

// Diffuse relighting of captured specular structure. A bounded heuristic; it cannot create missing reflections.
export function RefitProbeReflection(captured: float3, capturedIrradiance: float3, currentIrradiance: float3): float3 {
    return float3(captured.x * Clamp(currentIrradiance.x / Max(capturedIrradiance.x, 0.01), 0.0, 4.0),
        captured.y * Clamp(currentIrradiance.y / Max(capturedIrradiance.y, 0.01), 0.0, 4.0),
        captured.z * Clamp(currentIrradiance.z / Max(capturedIrradiance.z, 0.01), 0.0, 4.0));
}
