import { TextureDemandUv, WriteDecodedMaterial } from "./NeuralTextureMaterial";

@compute
@numthreads(32, 1, 1)
function TextureNeuralSampleMain(@builtin(groupId) tile: uint3, @builtin(groupIndex) lane: u32,
    @binding(0) readonly Latents: Texture2D<float4>,
    @binding(1) readonly Scalar: Texture2D<float4>,
    @binding(2) readonly Filter: Sampler,
    @binding(3) readonly Weights: StorageBuffer<u32>,
    @binding(4) readonly Demand: StorageBuffer<u32>,
    @binding(5) readwrite Output: StorageBuffer<u32>,
    @binding(6) readonly Coarse: Texture2D<float4>,
    @binding(7) readonly CoarseFilter: Sampler,
    @workgroup readwrite Values: WorkgroupArray<f32, 256>,
    @workgroup readwrite Hidden: WorkgroupArray<f32, 256>): void {
    let pixel: u32 = tile.x * 16 + tile.y * Demand[5] + lane;
    let live: bool = lane < 16 && pixel < Demand[0] * Demand[1];
    if (lane < 16) {
        let offset: u32 = lane * 16;
        var basis: float4 = float4(0.0, 0.0, 0.0, 0.0);
        var detail: f32 = 0.0;
        var coarse: float4 = float4(0.0, 0.0, 0.0, 0.0);
        let split: bool = Demand[4] != 0;
        if (live) {
            let uv: float2 = TextureDemandUv(pixel, Demand[0], Demand[1], Demand[2] != 0);
            basis = SampleLevel(Latents, Filter, uv, 0.0);
            detail = SampleLevel(Scalar, Filter, uv, 0.0).x;
            if (split) { coarse = SampleLevel(Coarse, CoarseFilter, uv, 0.0); }
        }
        Values[offset] = basis.x;
        Values[offset + 1] = basis.y;
        Values[offset + 2] = basis.z;
        Values[offset + 3] = basis.w;
        Values[offset + 4] = detail;
        Values[offset + 5] = 1.0;
        Values[offset + 6] = 0.0;
        Values[offset + 7] = 0.0;
        if (split) {
            Values[offset + 2] = detail;
            Values[offset + 3] = coarse.x;
            Values[offset + 4] = coarse.y;
            Values[offset + 5] = coarse.z;
            Values[offset + 6] = coarse.w;
            Values[offset + 7] = 1.0;
        }
        static for (let feature of Indices<8>()) {
            Values[offset + 8 + feature] = 0.0;
        }
    }
    CooperativeMatMul(Values, Weights, Hidden, lane, 0);
    static for (let stripe of Indices<8>()) {
        let index: u32 = lane + stripe * 32;
        Hidden[index] = Max(0.0, Hidden[index]);
    }
    CooperativeMatMul(Hidden, Weights, Values, lane, 128);
    if (live) {
        let at: u32 = lane * 16;
        let albedo: float3 = float3(Values[at], Values[at + 1], Values[at + 2]);
        let normal: float2 = float2(Values[at + 3], Values[at + 4]);
        WriteDecodedMaterial(Output, pixel, albedo, normal, Values[at + 5], Demand[3] != 0);
    }
}
