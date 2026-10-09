import { Shade } from "./ShapeLibrary";

@space(clip.position)
type ClipPosition = float4;

@space(world.position)
type WorldPosition = float3;

record Parameters {
    exposure: f32;
    eye: WorldPosition;
    kernel: Matrix<f32, 2, 2>;
}

@material
@binding(0)
record CustomMaterial {
    heading: f32;
    parameters: Parameters;
    gain: f32;
    samples: Array<f32, 2>;
}

stream Resources { @binding(0) material: CustomMaterial; }
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
function PixelMain(input: Varying, resources: Resources): Output {
    let value: f32 = Shade(input.value) + resources.material.parameters.kernel.at(0, 1)
        + resources.material.samples[0] + resources.material.gain + resources.material.parameters.eye.x;
    return { color: float4(value, value, value, 1.0) };
}
