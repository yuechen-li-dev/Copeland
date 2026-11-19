import { TextureDemandUv, WriteDecodedMaterial } from "./NeuralTextureMaterial";

@compute
@numthreads(64, 1, 1)
function TextureBcnSampleMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Color: Texture2D<float4>,
    @binding(1) readonly Normal: Texture2D<float4>,
    @binding(2) readonly Filter: Sampler,
    @binding(3) readonly Weights: StorageBuffer<u32>,
    @binding(4) readonly Demand: StorageBuffer<u32>,
    @binding(5) readwrite Output: StorageBuffer<u32>): void {
    let pixel: u32 = thread.x + thread.y * Demand[5];
    if (pixel >= Demand[0] * Demand[1]) { return; }
    let uv: float2 = TextureDemandUv(pixel, Demand[0], Demand[1], Demand[2] != 0);
    let color: float4 = SampleLevel(Color, Filter, uv, 0.0);
    let normal: float4 = SampleLevel(Normal, Filter, uv, 0.0);
    WriteDecodedMaterial(Output, pixel, float3(color.x, color.y, color.z), float2(normal.x, normal.y), color.w, Demand[3] != 0);
}
