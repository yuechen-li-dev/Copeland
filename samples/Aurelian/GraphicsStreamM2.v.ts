stream VertexInput {
    @location(0)
    position: float3;
    @location(1)
    uv: float2;
}

stream VertexOutput {
    @builtin(position)
    position: float4;
    @location(0)
    uv: float2;
}

stream PixelInput {
    @location(0)
    uv: float2;
}

stream PixelOutput {
    @target(0)
    color: float4;
}

function PassUv(value: float2): float2 {
    return value;
}

@vertex
function VertexMain(input: VertexInput): VertexOutput {
    return {
        position: float4(input.position, 1.0),
        uv: PassUv(input.uv),
    };
}

@pixel
function PixelMain(input: PixelInput): PixelOutput {
    return {
        color: float4(PassUv(input.uv), 0.0, 1.0),
    };
}
