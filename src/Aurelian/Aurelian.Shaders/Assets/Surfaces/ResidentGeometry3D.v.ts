import { Unit, Dot3, Scale3, Sub3 } from "./Lighting3D";

export record ResidentVertex {
    position: float3;
    normal: float3;
    color: float4;
    uv: float2;
    tangent: float4;
}
export record ResidentInstance {
    worldX: float4;
    worldY: float4;
    worldZ: float4;
    previousX: float4;
    previousY: float4;
    previousZ: float4;
    normalX: float4;
    normalY: float4;
    normalZ: float4;
    vertexBase: u32;
    indexBase: u32;
    commandBase: u32;
    primitiveKind: u32;
}
export record ResidentAttributes {
    world: float3;
    previous: float3;
    normal: float3;
    tangent: float4;
    color: float4;
    uv: float2;
}
export function PointRow(row: float4, point: float3): f32 {
    return row.x * point.x + row.y * point.y + row.z * point.z + row.w;
}
export function DirectionRow(row: float4, direction: float3): f32 {
    return row.x * direction.x + row.y * direction.y + row.z * direction.z;
}
export function TransformResidentVertex(vertex: ResidentVertex, instance: ResidentInstance): ResidentAttributes {
    let world: float3 = float3(PointRow(instance.worldX, vertex.position), PointRow(instance.worldY, vertex.position), PointRow(instance.worldZ, vertex.position));
    let previous: float3 = float3(PointRow(instance.previousX, vertex.position), PointRow(instance.previousY, vertex.position), PointRow(instance.previousZ, vertex.position));
    let normal: float3 = Unit(float3(DirectionRow(instance.normalX, vertex.normal), DirectionRow(instance.normalY, vertex.normal), DirectionRow(instance.normalZ, vertex.normal)));
    let localTangent: float3 = float3(vertex.tangent.x, vertex.tangent.y, vertex.tangent.z);
    let transformedTangent: float3 = float3(DirectionRow(instance.worldX, localTangent), DirectionRow(instance.worldY, localTangent), DirectionRow(instance.worldZ, localTangent));
    let tangent: float3 = Unit(Sub3(transformedTangent, Scale3(normal, Dot3(normal, transformedTangent))));
    return { world: world, previous: previous, normal: normal,
        tangent: float4(tangent.x, tangent.y, tangent.z, vertex.tangent.w), color: vertex.color, uv: vertex.uv };
}
export function Cross2(a: float2, b: float2): f32 {
    return a.x * b.y - a.y * b.x;
}
export function ProjectPoint(point: float3, x: float4, y: float4, z: float4, w: float4): float4 {
    return float4(PointRow(x, point), PointRow(y, point), PointRow(z, point), PointRow(w, point));
}
export function Weighted3(a: float3, b: float3, c: float3, weights: float3): float3 {
    return float3(a.x * weights.x + b.x * weights.y + c.x * weights.z,
        a.y * weights.x + b.y * weights.y + c.y * weights.z,
        a.z * weights.x + b.z * weights.y + c.z * weights.z);
}
export function Weighted4(a: float4, b: float4, c: float4, weights: float3): float4 {
    return float4(a.x * weights.x + b.x * weights.y + c.x * weights.z,
        a.y * weights.x + b.y * weights.y + c.y * weights.z,
        a.z * weights.x + b.z * weights.y + c.z * weights.z,
        a.w * weights.x + b.w * weights.y + c.w * weights.z);
}
export function Weighted2(a: float2, b: float2, c: float2, weights: float3): float2 {
    return float2(a.x * weights.x + b.x * weights.y + c.x * weights.z,
        a.y * weights.x + b.y * weights.y + c.y * weights.z);
}
