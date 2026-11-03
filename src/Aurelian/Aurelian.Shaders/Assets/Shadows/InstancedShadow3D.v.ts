@space(clip.position)
type ClipPosition4 = float4;

@material
@binding(0)
record ShadowMaterial {
    clipX: float4;
    clipY: float4;
    clipZ: float4;
    clipW: float4;
}
stream ShadowResources { @binding(0) shadow: ShadowMaterial; }
stream ShadowInput {
    @location(0) position: float3;
    @location(5) worldX: float4;
    @location(6) worldY: float4;
    @location(7) worldZ: float4;
}
stream ShadowVaryings {
    @builtin(position) position: ClipPosition4;
    @location(0) depth: f32;
}
stream ShadowOutput { @target(0) color: float4; }
function ShadowRow(row: float4, p: float3): f32 {
    return row.x * p.x + row.y * p.y + row.z * p.z + row.w;
}
@vertex
function VertexMain(input: ShadowInput, resources: ShadowResources): ShadowVaryings {
    const world: float3 = float3(ShadowRow(input.worldX, input.position),
        ShadowRow(input.worldY, input.position), ShadowRow(input.worldZ, input.position));
    const z: f32 = ShadowRow(resources.shadow.clipZ, world);
    return { position: float4(ShadowRow(resources.shadow.clipX, world), ShadowRow(resources.shadow.clipY, world),
        z, ShadowRow(resources.shadow.clipW, world)), depth: z };
}
@pixel
function PixelMain(input: ShadowVaryings): ShadowOutput {
    // Fragment position contains post-divide Vulkan depth for both perspective
    // spot lights and the existing orthographic directional light.
    return { color: float4(input.position.z, input.position.z, input.position.z, 1.0) };
}
