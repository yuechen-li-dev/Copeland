import { Shade } from "./GenericLibrary";

@space(clip.position)
type ClipPosition = float4;
stream Input { @location(0) position: float3; }
stream Varying {
    @builtin(position) position: ClipPosition;
    @location(0) value: f32;
}
stream Output { @target(0) color: float4; }

@vertex
function VertexMain(input: Input): Varying {
    return { position: float4(input.position, 1.0), value: input.position.x };
}

@pixel
function PixelMain(input: Varying): Output {
    let value: f32 = Shade(input.value);
    return { color: float4(value, value, value, 1.0) };
}
