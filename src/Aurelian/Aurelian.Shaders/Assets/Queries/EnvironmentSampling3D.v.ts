import { EnvironmentUv } from "./EnvironmentCoordinates3D";

function Texel(readonly image: StorageBuffer<f32>, x: u32, y: u32, width: u32): float3 {
    let offset: u32 = (y * width + x) * 4;
    return float3(image[offset], image[offset + 1], image[offset + 2]);
}
export function ReadEnvironmentRadiance(readonly image: StorageBuffer<f32>, readonly parameters: StorageBuffer<f32>, direction: float3): float3 {
    if (parameters[45] < 0.5) { return float3(0.0, 0.0, 0.0); }
    let uv: float2 = EnvironmentUv(direction);
    let width: u32 = U32(parameters[28]);
    let height: u32 = U32(parameters[29]);
    // Match the source texture's linear, clamp-to-edge sampler in environment compilation.
    let x: f32 = Clamp(uv.x * parameters[28] - 0.5, 0.0, parameters[28] - 1.0);
    let y: f32 = Clamp(uv.y * parameters[29] - 0.5, 0.0, parameters[29] - 1.0);
    let firstX: u32 = U32(Floor(x));
    let firstY: u32 = U32(Floor(y));
    let nextX: u32 = U32(Min(Convert<f32>(firstX) + 1.0, parameters[28] - 1.0));
    let nextY: u32 = U32(Min(Convert<f32>(firstY) + 1.0, parameters[29] - 1.0));
    let fractionX: f32 = x - Floor(x);
    let fractionY: f32 = y - Floor(y);
    let top: float3 = Texel(image, firstX, firstY, width) * (1.0 - fractionX) + Texel(image, nextX, firstY, width) * fractionX;
    let bottom: float3 = Texel(image, firstX, nextY, width) * (1.0 - fractionX) + Texel(image, nextX, nextY, width) * fractionX;
    return (top * (1.0 - fractionY) + bottom * fractionY) * parameters[30];
}
export function EnvironmentSamplePdf(readonly parameters: StorageBuffer<f32>, direction: float3): f32 {
    if (parameters[47] < 0.5) { return 0.07957747155; }
    let uv: float2 = EnvironmentUv(direction);
    let width: u32 = U32(parameters[43]);
    let height: u32 = U32(parameters[44]);
    let x: u32 = U32(Min(Floor(uv.x * parameters[43]), parameters[43] - 1.0));
    let y: u32 = U32(Min(Floor(uv.y * parameters[44]), parameters[44] - 1.0));
    let bin: u32 = y * width + x;
    let offset: u32 = U32(parameters[42]);
    var previous: f32 = 0.0;
    if (bin > 0) { previous = parameters[offset + bin - 1]; }
    let probability: f32 = parameters[offset + bin] - previous;
    let solidAngle: f32 = 6.2831853 / parameters[43]
        * (Cos(3.14159265 * Convert<f32>(y) / parameters[44]) - Cos(3.14159265 * (Convert<f32>(y) + 1.0) / parameters[44]));
    // The uniform component guarantees support for bilinear bleed into dark CDF bins.
    return 0.05 * 0.07957747155 + 0.95 * probability / solidAngle;
}
export function SampleEnvironmentDirection(readonly parameters: StorageBuffer<f32>, choice: f32, u: f32, v: f32): float3 {
    var phi: f32 = (u - 0.5) * 6.2831853;
    var cosine: f32 = 1.0 - 2.0 * v;
    if (parameters[47] > 0.5 && choice >= 0.05) {
        let target: f32 = (choice - 0.05) / 0.95;
        let width: u32 = U32(parameters[43]);
        let count: u32 = width * U32(parameters[44]);
        let offset: u32 = U32(parameters[42]);
        var lower: u32 = 0;
        var upper: u32 = count;
        static for (let step of Indices<12>()) {
            if (lower < upper) {
                let middle: u32 = (lower + upper) / 2;
                if (target < parameters[offset + middle]) { upper = middle; }
                else { lower = middle + 1; }
            }
        }
        if (lower >= count) { lower = count - 1; }
        let row: u32 = lower / width;
        phi = ((Convert<f32>(lower % width) + u) / parameters[43] - 0.5) * 6.2831853;
        let top: f32 = Cos(3.14159265 * Convert<f32>(row) / parameters[44]);
        let bottom: f32 = Cos(3.14159265 * (Convert<f32>(row) + 1.0) / parameters[44]);
        cosine = top * (1.0 - v) + bottom * v;
    }
    let sine: f32 = Sqrt(Max(1.0 - cosine * cosine, 0.0));
    return float3(Cos(phi) * sine, cosine, Sin(phi) * sine);
}
