import { EnvironmentUv } from "./EnvironmentCoordinates3D";

function Texel(readonly image: StorageBuffer<f32>, x: u32, y: u32, width: u32): float3 {
    const offset: u32 = (y * width + x) * 4;
    return float3(image[offset], image[offset + 1], image[offset + 2]);
}
export function ReferenceEnvironment(readonly image: StorageBuffer<f32>, readonly parameters: StorageBuffer<f32>, direction: float3): float3 {
    if (parameters[45] < 0.5) { return float3(0.0, 0.0, 0.0); }
    const uv: float2 = EnvironmentUv(direction);
    const width: u32 = U32(parameters[28]);
    const height: u32 = U32(parameters[29]);
    // Match the source texture's linear, clamp-to-edge sampler in environment compilation.
    const x: f32 = Clamp(uv.x * parameters[28] - 0.5, 0.0, parameters[28] - 1.0);
    const y: f32 = Clamp(uv.y * parameters[29] - 0.5, 0.0, parameters[29] - 1.0);
    const firstX: u32 = U32(Floor(x));
    const firstY: u32 = U32(Floor(y));
    const nextX: u32 = U32(Min(Convert<f32>(firstX) + 1.0, parameters[28] - 1.0));
    const nextY: u32 = U32(Min(Convert<f32>(firstY) + 1.0, parameters[29] - 1.0));
    const fractionX: f32 = x - Floor(x);
    const fractionY: f32 = y - Floor(y);
    const top: float3 = Texel(image, firstX, firstY, width) * (1.0 - fractionX) + Texel(image, nextX, firstY, width) * fractionX;
    const bottom: float3 = Texel(image, firstX, nextY, width) * (1.0 - fractionX) + Texel(image, nextX, nextY, width) * fractionX;
    return (top * (1.0 - fractionY) + bottom * fractionY) * parameters[30];
}
export function ReferenceEnvironmentPdf(readonly parameters: StorageBuffer<f32>, direction: float3): f32 {
    if (parameters[47] < 0.5) { return 0.07957747155; }
    const uv: float2 = EnvironmentUv(direction);
    const width: u32 = U32(parameters[43]);
    const height: u32 = U32(parameters[44]);
    const x: u32 = U32(Min(Floor(uv.x * parameters[43]), parameters[43] - 1.0));
    const y: u32 = U32(Min(Floor(uv.y * parameters[44]), parameters[44] - 1.0));
    const bin: u32 = y * width + x;
    const offset: u32 = U32(parameters[42]);
    var previous: f32 = 0.0;
    if (bin > 0) { previous = parameters[offset + bin - 1]; }
    const probability: f32 = parameters[offset + bin] - previous;
    const solidAngle: f32 = 6.2831853 / parameters[43]
        * (Cos(3.14159265 * Convert<f32>(y) / parameters[44]) - Cos(3.14159265 * (Convert<f32>(y) + 1.0) / parameters[44]));
    // The uniform component guarantees support for bilinear bleed into dark CDF bins.
    return 0.05 * 0.07957747155 + 0.95 * probability / solidAngle;
}
export function SampleReferenceEnvironment(readonly parameters: StorageBuffer<f32>, choice: f32, u: f32, v: f32): float3 {
    var phi: f32 = (u - 0.5) * 6.2831853;
    var cosine: f32 = 1.0 - 2.0 * v;
    if (parameters[47] > 0.5 && choice >= 0.05) {
        const target: f32 = (choice - 0.05) / 0.95;
        const width: u32 = U32(parameters[43]);
        const count: u32 = width * U32(parameters[44]);
        const offset: u32 = U32(parameters[42]);
        var lower: u32 = 0;
        var upper: u32 = count;
        static for (let step of Indices<12>()) {
            if (lower < upper) {
                const middle: u32 = (lower + upper) / 2;
                if (target < parameters[offset + middle]) { upper = middle; }
                else { lower = middle + 1; }
            }
        }
        if (lower >= count) { lower = count - 1; }
        const row: u32 = lower / width;
        phi = ((Convert<f32>(lower % width) + u) / parameters[43] - 0.5) * 6.2831853;
        const top: f32 = Cos(3.14159265 * Convert<f32>(row) / parameters[44]);
        const bottom: f32 = Cos(3.14159265 * (Convert<f32>(row) + 1.0) / parameters[44]);
        cosine = top * (1.0 - v) + bottom * v;
    }
    const sine: f32 = Sqrt(Max(1.0 - cosine * cosine, 0.0));
    return float3(Cos(phi) * sine, cosine, Sin(phi) * sine);
}
