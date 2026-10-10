import { ProbeDiffuse, RefitProbeReflection } from "./DiffuseProbeLighting";
import { EnvironmentResponse, Band, EncodeOcta } from "./SurfaceLighting";
import { Add3, Sub3, Mul3, Scale3, Dot3, Unit } from "./Lighting3D";
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
    @binding(1) hits: Texture2D<float4>;
    @binding(2) hitsSampler: Sampler;
    @binding(3) surfaces: Texture2D<float4>;
    @binding(4) surfacesSampler: Sampler;
    @binding(5) cache: Texture2D<float4>;
    @binding(6) cacheSampler: Sampler;
    @binding(7) irradiance: Texture2D<float4>;
    @binding(8) irradianceSampler: Sampler;
    @binding(9) visibility: Texture2D<float4>;
    @binding(10) visibilitySampler: Sampler;
    @binding(11) environment: Texture2D<float4>;
    @binding(12) environmentSampler: Sampler;
}
stream Output { @target(0) color: float4; }
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    const pixel: f32 = Floor(input.uv.y * resources.material.batch.y) * resources.material.batch.x
        + Floor(input.uv.x * resources.material.batch.x);
    const metadata: float4 = ReadRayHit(resources.hits, resources.hitsSampler, pixel, 0.0, resources.material.parameters.z);
    if (metadata.z < 0.5) {
        if (resources.material.lightColor.w > 0.5) {
            return { color: float4(0.0, 0.0, 0.0, 0.0) };
        }
        return { color: float4(0.025, 0.035, 0.055, 0.0) };
    }
    const bary: float4 = ReadRayHit(resources.hits, resources.hitsSampler, pixel, 1.0, resources.material.parameters.z);
    const p: float4 = ReadRayHit(resources.hits, resources.hitsSampler, pixel, 2.0, resources.material.parameters.z);
    const n: float4 = ReadRayHit(resources.hits, resources.hitsSampler, pixel, 3.0, resources.material.parameters.z);
    const point: float3 = float3(p.x, p.y, p.z);
    const normal: float3 = float3(n.x, n.y, n.z);
    const size: f32 = resources.material.parameters.x;
    const x: f32 = Clamp(bary.y * (size - 1.0), 0.0, size - 1.0);
    const y: f32 = Clamp(bary.z * (size - 1.0), 0.0, size - 1.0);
    const cell: f32 = metadata.w * size * size + y * size + x;
    const material: float4 = Sample(resources.surfaces, resources.surfacesSampler,
        float2(0.625, (cell + 0.5) / (resources.material.parameters.y * size * size)));
    const albedo: float3 = float3(material.x, material.y, material.z);
    const cached: float4 = Sample(resources.cache, resources.cacheSampler,
        float2((x + 0.5) / size, (metadata.w * size + y + 0.5) / (resources.material.parameters.y * size)));
    var direct: float3 = float3(0.0, 0.0, 0.0);
    if (cached.w == resources.material.parameters.w) {
        direct = float3(cached.x, cached.y, cached.z);
    }
    var indirect: float3 = float3(0.0, 0.0, 0.0);
    var response: float4 = float4(0.0, 0.0, 0.0, 0.0);
    if (resources.material.batch.z > 0.5) {
        response = ProbeDiffuse(resources.irradiance, resources.visibility, resources.irradianceSampler,
            point, normal, resources.material.gridMinimum, resources.material.gridMaximum,
            resources.material.gridSize, resources.material.parameters.w, true);
        indirect = Mul3(albedo, float3(response.x, response.y, response.z));
    }
    var result: float3 = direct;
    if (resources.material.batch.w > 0.5) {
        result = Add3(result, indirect);
    }
    if (resources.material.batch.w > 1.5) {
        const eye: float3 = float3(resources.material.sky.x, resources.material.sky.y, resources.material.sky.z);
        const view: float3 = Unit(Sub3(eye, point));
        const nv: f32 = Max(Dot3(normal, view), 0.0001);
        const reflected: float3 = Sub3(Scale3(normal, 2.0 * nv), view);
        // The dielectric specular component uses the same integrated GGX/DFG atlas as game materials.
        var specular: float3 = EnvironmentResponse(resources.environment, resources.environmentSampler,
            float3(0.0, 0.0, 0.0), normal, reflected, nv, 0.0, 0.35);
        if (resources.material.batch.w > 2.5) {
            const captured: float3 = Band(resources.environment, resources.environmentSampler, EncodeOcta(normal), 6.0);
            if (response.w > 0.5) {
                specular = RefitProbeReflection(specular, captured,
                    float3(response.x, response.y, response.z));
            } else {
                specular = float3(0.0, 0.0, 0.0);
            }
        }
        result = Add3(result, specular);
    }
    return { color: float4(result.x, result.y, result.z, 1.0) };
}
