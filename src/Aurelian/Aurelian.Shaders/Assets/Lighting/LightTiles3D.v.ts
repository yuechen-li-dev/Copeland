import { AddMaskBit } from "./SurfaceLighting";

@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record PassMaterial {
    clipX: float4;
    clipY: float4;
    clipZ: float4;
    clipW: float4;
    parameters: float4;
}
stream Resources {
    @binding(0) material: PassMaterial;
    @binding(1) lights: Texture2D<float4>;
    @binding(2) lightsSampler: Sampler;
}
stream Input { @location(0) position: float2; }
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
stream Output { @target(0) color: float4; }
@vertex
function VertexMain(input: Input): Varyings {
    return {
        position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5),
    };
}

function Row(row: float4, point: float3): f32 {
    return row.x * point.x + row.y * point.y + row.z * point.z + row.w;
}
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    var masks: float4 = float4(0.0, 0.0, 0.0, 0.0);
    const tileMinimum: float2 = float2(input.uv.x - resources.material.parameters.y * 0.5,
        input.uv.y - resources.material.parameters.z * 0.5);
    const tileMaximum: float2 = float2(input.uv.x + resources.material.parameters.y * 0.5,
        input.uv.y + resources.material.parameters.z * 0.5);
    for (var index: u32 = 0; index < 32; index = index + 1) {
        if (Convert<f32>(index) < resources.material.parameters.x) {
            const sphere: float4 = Sample(resources.lights, resources.lightsSampler,
                float2(0.125, (Convert<f32>(index) + 0.5) / 32.0));
            var minimum: float2 = float2(1.0, 1.0);
            var maximum: float2 = float2(0.0, 0.0);
            var crossing: bool = false;
            for (var corner: u32 = 0; corner < 8; corner = corner + 1) {
                var x: f32 = -sphere.w;
                var y: f32 = -sphere.w;
                var z: f32 = -sphere.w;
                if (Convert<f32>(corner) - Floor(Convert<f32>(corner) / 2.0) * 2.0 > 0.5) { x = sphere.w; }
                if (Floor(Convert<f32>(corner) / 2.0) - Floor(Convert<f32>(corner) / 4.0) * 2.0 > 0.5) { y = sphere.w; }
                if (corner >= 4) { z = sphere.w; }
                const p: float3 = float3(sphere.x + x, sphere.y + y, sphere.z + z);
                const w: f32 = Row(resources.material.clipW, p);
                if (w <= 0.0001) {
                    crossing = true;
                } else {
                    const u: f32 = Row(resources.material.clipX, p) / w * 0.5 + 0.5;
                    const v: f32 = Row(resources.material.clipY, p) / w * 0.5 + 0.5;
                    minimum = float2(Min(minimum.x, u), Min(minimum.y, v));
                    maximum = float2(Max(maximum.x, u), Max(maximum.y, v));
                }
            }
            if (crossing || (maximum.x >= tileMinimum.x && minimum.x <= tileMaximum.x
                && maximum.y >= tileMinimum.y && minimum.y <= tileMaximum.y)) {
                const value: f32 = Pow(2.0, (Convert<f32>(index) - Floor(Convert<f32>(index) / 8.0) * 8.0));
                masks = AddMaskBit(masks, index, value);
            }
        }
    }
    return { color: masks };
}
