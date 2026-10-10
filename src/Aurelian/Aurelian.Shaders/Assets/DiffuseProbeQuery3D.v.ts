import { ProbeDiffuseConnected } from "./DiffuseProbeLighting";
@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record ProbeMaterial {
    parameters: float4;
    batch: float4;
    lightPosition: float4;
    lightColor: float4;
    gridMinimum: float4;
    gridMaximum: float4;
    gridSize: float4;
    sky: float4;
}
stream Input { @location(0) position: float2; }
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
@vertex
function VertexMain(input: Input): Varyings {
    return {
        position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5),
    };
}
stream Resources {
    @binding(0) material: ProbeMaterial;
    @binding(1) points: Texture2D<float4>;
    @binding(2) pointsSampler: Sampler;
    @binding(3) irradiance: Texture2D<float4>;
    @binding(4) irradianceSampler: Sampler;
    @binding(5) visibility: Texture2D<float4>;
    @binding(6) visibilitySampler: Sampler;
    @binding(7) connections: Texture2D<float4>;
    @binding(8) connectionsSampler: Sampler;
}
stream Output { @target(0) color: float4; }
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    const p: float4 = Sample(resources.points, resources.pointsSampler, float2(0.25, input.uv.y));
    const n: float4 = Sample(resources.points, resources.pointsSampler, float2(0.75, input.uv.y));
    if (resources.material.batch.z < 0.5) {
        return { color: float4(0.0, 0.0, 0.0, 0.0) };
    }
    var mask: f32 = 255.0;
    if (resources.material.batch.w > 1.5) {
        const packet: float4 = Sample(resources.connections, resources.connectionsSampler, float2(0.5, input.uv.y));
        if (packet.w != resources.material.parameters.w) {
            return { color: float4(0.0, 0.0, 0.0, 0.0) };
        }
        mask = packet.x;
    }
    const response: float4 = ProbeDiffuseConnected(resources.irradiance, resources.visibility, resources.irradianceSampler,
        float3(p.x, p.y, p.z), float3(n.x, n.y, n.z), resources.material.gridMinimum, resources.material.gridMaximum,
        resources.material.gridSize, resources.material.parameters.w, resources.material.batch.w > 0.5, mask);
    return { color: response };
}
