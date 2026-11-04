import { Sub3, Dot3, Scale3, Add3, Mul3 } from "./Lighting3D";
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
    @binding(1) surfaces: Texture2D<float4>;
    @binding(2) surfacesSampler: Sampler;
    @binding(3) hits: Texture2D<float4>;
    @binding(4) hitsSampler: Sampler;
    @binding(5) previous: Texture2D<float4>;
    @binding(6) previousSampler: Sampler;
}
stream Output { @target(0) color: float4; }
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    let resolution: f32 = resources.material.parameters.x;
    let height: f32 = resolution * resources.material.parameters.y;
    let index: f32 = Floor(input.uv.y * height) * resolution + Floor(input.uv.x * resolution);
    let generation: f32 = resources.material.parameters.w;
    let old: float4 = Sample(resources.previous, resources.previousSampler, input.uv);
    if (index < resources.material.batch.x || index >= resources.material.batch.x + resources.material.batch.y) {
        if (old.w == generation) {
            return { color: old };
        }
        return { color: float4(0.0, 0.0, 0.0, 0.0) };
    }
    let row: f32 = (index + 0.5) / (height * resolution);
    let p: float4 = Sample(resources.surfaces, resources.surfacesSampler, float2(0.125, row));
    let n: float4 = Sample(resources.surfaces, resources.surfacesSampler, float2(0.375, row));
    let albedo: float4 = Sample(resources.surfaces, resources.surfacesSampler, float2(0.625, row));
    let emission: float4 = Sample(resources.surfaces, resources.surfacesSampler, float2(0.875, row));
    let hit: float4 = ReadRayHit(resources.hits, resources.hitsSampler,
        index - resources.material.batch.x, 0.0, resources.material.parameters.z);
    let delta: float3 = Sub3(float3(resources.material.lightPosition.x, resources.material.lightPosition.y,
        resources.material.lightPosition.z), float3(p.x, p.y, p.z));
    let squared: f32 = Max(Dot3(delta, delta), 0.01);
    let cosine: f32 = Max(Dot3(float3(n.x, n.y, n.z), Scale3(delta, 1.0 / Sqrt(squared))), 0.0);
    var direct: f32 = 0.0;
    if (hit.z < 0.5) {
        direct = cosine * resources.material.lightPosition.w / (3.14159265 * squared);
    }
    let radiance: float3 = Add3(float3(emission.x, emission.y, emission.z),
        Scale3(Mul3(float3(albedo.x, albedo.y, albedo.z), float3(resources.material.lightColor.x,
            resources.material.lightColor.y, resources.material.lightColor.z)), direct));
    return { color: float4(radiance.x, radiance.y, radiance.z, generation) };
}
