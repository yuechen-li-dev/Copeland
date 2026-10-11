// Packed storage preserves the existing 24/8/19/10-word transport.
// The compiler owns offsets; joint indices retain the explicit legacy f32 encoding.
record Influence {
    joint: f32;
    weight: f32;
}
record RestVertex {
    position: float3;
    normal: float3;
    influences: Array<Influence, 6>;
    leftCorrection: float3;
    rightCorrection: float3;
}
record DualQuaternion {
    real: float4;
    dual: float4;
}
record PoseHeader {
    count: f32;
    leftCorrection: f32;
    rightCorrection: f32;
    world: Array<f32, 16>;
}
record SolidVertex {
    position: float3;
    normal: float3;
    color: float4;
}

function NormalizeDualQuaternion(value: DualQuaternion): DualQuaternion {
    let inverseLength: f32 = 1.0 / Length(value.real);
    let real = value.real * inverseLength;
    let dual = value.dual * inverseLength;
    return { real: real, dual: dual - real * Dot(real, dual) };
}
function Rotate(real: float4, value: float3): float3 {
    let crossed = Cross(real.xyz, value);
    return value + crossed * (2.0 * real.w) + Cross(real.xyz, crossed) * 2.0;
}
function TransformPoint(value: DualQuaternion, point: float3): float3 {
    let translation = (value.dual.xyz * value.real.w - value.real.xyz * value.dual.w
        + Cross(value.real.xyz, value.dual.xyz)) * 2.0;
    return Rotate(value.real, point) + translation;
}
function WorldPoint(world: Array<f32, 16>, value: float3): float3 {
    return float3(value.x * world[0] + value.y * world[4] + value.z * world[8] + world[12],
        value.x * world[1] + value.y * world[5] + value.z * world[9] + world[13],
        value.x * world[2] + value.y * world[6] + value.z * world[10] + world[14]);
}
function WorldDirection(world: Array<f32, 16>, value: float3): float3 {
    return float3(value.x * world[0] + value.y * world[4] + value.z * world[8],
        value.x * world[1] + value.y * world[5] + value.z * world[9],
        value.x * world[2] + value.y * world[6] + value.z * world[10]);
}
function Blend(readonly palette: StorageBuffer<DualQuaternion>, vertex: RestVertex): DualQuaternion {
    let reference = palette[U32(vertex.influences[0].joint)].real;
    var blended: DualQuaternion = {
        real: float4(0.0, 0.0, 0.0, 0.0),
        dual: float4(0.0, 0.0, 0.0, 0.0),
    };
    static for (let index of Indices<6>()) {
        let influence = vertex.influences[index];
        let joint = palette[U32(influence.joint)];
        var weight = influence.weight;
        if (Dot(reference, joint.real) < 0.0) {
            weight = -weight;
        }
        blended = {
            real: blended.real + joint.real * weight,
            dual: blended.dual + joint.dual * weight,
        };
    }
    return NormalizeDualQuaternion(blended);
}

@compute
@numthreads(64, 1, 1)
function SkinMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Rest: StorageBuffer<RestVertex>,
    @binding(1) readonly Palette: StorageBuffer<DualQuaternion>,
    @binding(2) readonly Pose: StorageBuffer<PoseHeader>,
    @binding(3) readwrite Output: StorageBuffer<SolidVertex>): void {
    let pose = Pose[0];
    if (thread.x >= U32(pose.count)) {
        return;
    }
    let vertex = Rest[thread.x];
    let transform = Blend(Palette, vertex);
    let corrected = vertex.position + vertex.leftCorrection * pose.leftCorrection
        + vertex.rightCorrection * pose.rightCorrection;
    let position = TransformPoint(transform, corrected);
    let normal = Rotate(transform.real, vertex.normal);
    // Anatomical Z-up millimetres to engine Y-up metres.
    let metres = float3(position.x, position.z, -position.y) * 0.001;
    let direction = float3(normal.x, normal.z, -normal.y);
    Output[thread.x] = {
        position: WorldPoint(pose.world, metres),
        normal: WorldDirection(pose.world, direction),
        color: float4(0.67, 0.69, 0.72, 1.0),
    };
}
