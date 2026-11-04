import { DiffuseResponse } from "./Lighting3D";

// Isolated diffuse lobe, deliberately excluding GGX and Fresnel layering.
@compute
@numthreads(64, 1, 1)
function ReferenceFurnaceMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readwrite Output: StorageBuffer<f32>): void {
    if (thread.x < 80) {
        let offset: u32 = thread.x * 4;
        let model: f32 = Input[offset];
        let roughness: f32 = Input[offset + 1];
        let nv: f32 = Input[offset + 2];
        let view: float3 = float3(Sqrt(Max(1.0 - nv * nv, 0.0)), nv, 0.0);
        var total: f32 = 0.0;
        for (let polar of BoundedIndices<128>(128)) {
            let nl: f32 = Sqrt((Convert<f32>(polar) + 0.5) / 128.0);
            let tangent: f32 = Sqrt(Max(1.0 - nl * nl, 0.0));
            for (let azimuth of BoundedIndices<256>(256)) {
                let angle: f32 = 6.2831853 * (Convert<f32>(azimuth) + 0.5) / 256.0;
                let incoming: float3 = float3(tangent * Cos(angle), nl, tangent * Sin(angle));
                let response: float3 = DiffuseResponse(float3(1.0, 1.0, 1.0), float3(0.0, 1.0, 0.0),
                    view, incoming, roughness, model);
                total = total + response.x / 32768.0;
            }
        }
        Output[offset] = total;
        Output[offset + 1] = total;
        Output[offset + 2] = total;
        Output[offset + 3] = 1.0;
    }
}
