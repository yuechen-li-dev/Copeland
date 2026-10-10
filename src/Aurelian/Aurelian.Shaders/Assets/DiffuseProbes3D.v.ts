import { DecodeOcta } from "./SurfaceLighting";
import { Dot3, Add3, Scale3 } from "./Lighting3D";
import { ReadRayHit } from "./DiffuseRayHit";
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
    @binding(1) cache: Texture2D<float4>;
    @binding(2) cacheSampler: Sampler;
    @binding(3) hits: Texture2D<float4>;
    @binding(4) hitsSampler: Sampler;
    @binding(5) directions: Texture2D<float4>;
    @binding(6) directionsSampler: Sampler;
    @binding(7) previous: Texture2D<float4>;
    @binding(8) previousSampler: Sampler;
    @binding(9) previousVisibility: Texture2D<float4>;
    @binding(10) previousVisibilitySampler: Sampler;
}
stream Output {
    @target(0) irradiance: float4;
    @target(1) visibility: float4;
}
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    const count: f32 = resources.material.gridSize.x * resources.material.gridSize.y * resources.material.gridSize.z;
    const row: f32 = Floor(input.uv.y * count * 8.0);
    const probe: f32 = Floor(row / 8.0);
    const generation: f32 = resources.material.parameters.w;
    if (probe < resources.material.batch.x || probe >= resources.material.batch.x + resources.material.batch.y) {
        const old: float4 = Sample(resources.previous, resources.previousSampler, input.uv);
        const oldVisibility: float4 = Sample(resources.previousVisibility, resources.previousVisibilitySampler, input.uv);
        if (old.w == generation) {
            return { irradiance: old, visibility: oldVisibility };
        }
        return { irradiance: float4(0.0, 0.0, 0.0, 0.0), visibility: float4(0.0, 0.0, 0.0, 0.0) };
    }
    const normal: float3 = DecodeOcta(float2(input.uv.x, (row - probe * 8.0 + 0.5) / 8.0));
    var accumulated: float3 = float3(0.0, 0.0, 0.0);
    var mean: f32 = 0.0;
    var second: f32 = 0.0;
    var momentWeight: f32 = 0.0;
    var valid: f32 = generation;
    for (var ray: u32 = 0; ray < 128; ray = ray + 1) {
        if (Convert<f32>(ray) < resources.material.gridSize.w) {
            const direction: float4 = Sample(resources.directions, resources.directionsSampler,
                float2(0.5, (Convert<f32>(ray) + 0.5) / resources.material.gridSize.w));
            const hitIndex: f32 = (probe - resources.material.batch.x) * resources.material.gridSize.w + Convert<f32>(ray);
            const metadata: float4 = ReadRayHit(resources.hits, resources.hitsSampler, hitIndex, 0.0, resources.material.parameters.z);
            const distanceBary: float4 = ReadRayHit(resources.hits, resources.hitsSampler, hitIndex, 1.0, resources.material.parameters.z);
            var radiance: float3 = float3(resources.material.sky.x, resources.material.sky.y, resources.material.sky.z);
            var distance: f32 = resources.material.sky.w;
            if (metadata.z > 0.5) {
                const facing: float4 = ReadRayHit(resources.hits, resources.hitsSampler, hitIndex, 3.0, resources.material.parameters.z);
                if (facing.w < 0.0) {
                    // Interior/back-facing probes must not inject energy through solids.
                    valid = 0.0;
                }
                distance = distanceBary.x;
                const size: f32 = resources.material.parameters.x;
                const x: f32 = Clamp(distanceBary.y * (size - 1.0), 0.0, size - 1.0);
                const y: f32 = Clamp(distanceBary.z * (size - 1.0), 0.0, size - 1.0);
                const cached: float4 = Sample(resources.cache, resources.cacheSampler,
                    float2((x + 0.5) / size, (metadata.w * size + y + 0.5) / (size * resources.material.parameters.y)));
                radiance = float3(cached.x, cached.y, cached.z);
                if (cached.w != generation) {
                    valid = 0.0;
                }
            }
            const cosine: f32 = Max(Dot3(normal, float3(direction.x, direction.y, direction.z)), 0.0);
            accumulated = Add3(accumulated, Scale3(radiance, cosine * 4.0 / resources.material.gridSize.w));
            const weight: f32 = Pow(cosine, 16.0);
            momentWeight = momentWeight + weight;
            mean = mean + distance * weight;
            second = second + distance * distance * weight;
        }
    }
    mean = mean / Max(momentWeight, 0.000001);
    second = second / Max(momentWeight, 0.000001);
    return {
        irradiance: float4(accumulated.x, accumulated.y, accumulated.z, valid),
        visibility: float4(mean, second, 0.0, valid),
    };
}
