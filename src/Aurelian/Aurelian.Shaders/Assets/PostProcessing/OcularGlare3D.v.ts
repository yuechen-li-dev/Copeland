@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record OcularMaterial {
    // Relative UV scale and inverse source extent.
    source: float4;
    // Relative lower-level UV scale, include-lower flag, downsample flag.
    lower: float4;
    // Direct and local scattered mass.
    mass: float4;
}
stream Resources {
    @binding(0) material: OcularMaterial;
    @binding(1) source: Texture2D<float4>;
    @binding(2) sourceSampler: Sampler;
    @binding(3) lower: Texture2D<float4>;
    @binding(4) lowerSampler: Sampler;
}
stream Input { @location(0) position: float2; }
stream Varyings { @builtin(position) position: ClipPosition4; @location(0) uv: float2; }
stream Output { @target(0) color: float4; }
@vertex
function VertexMain(input: Input): Varyings {
    return { position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5) };
}
@pixel
function PixelMain(input: Varyings, r: Resources): Output {
    let source: float4 = r.material.source;
    let lower: float4 = r.material.lower;
    let mass: float4 = r.material.mass;
    let uv: float2 = float2(input.uv.x * source.x, input.uv.y * source.y);
    if (lower.w > 0.5) {
        return { color: Sample(r.source, r.sourceSampler, uv) };
    }

    var color: float4 = float4(0.0, 0.0, 0.0, 0.0);
    if (mass.x > 0.0) {
        let direct: float4 = Sample(r.source, r.sourceSampler, uv);
        color = direct * float4(mass.x, mass.x, mass.x, 0.0);
    }
    let tap: f32 = mass.y * 0.25;
    static for (let y of [-0.5, 0.5]) {
        static for (let x of [-0.5, 0.5]) {
            let value: float4 = Sample(r.source, r.sourceSampler,
                float2(uv.x + x * source.z, uv.y + y * source.w));
            color = float4(color.x + value.x * tap, color.y + value.y * tap, color.z + value.z * tap, 0.0);
        }
    }
    if (lower.z > 0.5) {
        let broad: float4 = Sample(r.lower, r.lowerSampler, float2(input.uv.x * lower.x, input.uv.y * lower.y));
        color = float4(color.x + broad.x, color.y + broad.y, color.z + broad.z, 0.0);
    }
    return { color: float4(color.x, color.y, color.z, 1.0) };
}
