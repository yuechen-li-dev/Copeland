namespace Aurelian.Shaders.Compute;

/// <summary>Compiler-owned lowering adapted from Oct internal/sdslv/emit/hlsl.
/// The source-level command is statement-only; opaque mutable query state never escapes.</summary>
internal static class RayQueryHlslEmitter
{
    // Each ray: three float4 lanes after a four-word header (count in uint bits).
    // Each triangle: three float4 vertices, w stores layer/mask/reserved uint bits.
    // Each sphere: centre/radius, layer/mask/reserved/reserved (eight words).
    // Each hit: four float4 lanes: metadata uint bits, t/barycentrics, point, normal.
    internal const string Helper = """
        void AurelianTraceClosest(
            RaytracingAccelerationStructure scene,
            StructuredBuffer<float> spheres,
            StructuredBuffer<float> rays,
            RWStructuredBuffer<float> hits,
            StructuredBuffer<float> triangles,
            uint index)
        {
            if (index >= asuint(rays[0])) return;
            uint base = 4u + index * 12u;
            RayDesc ray;
            ray.Origin = float3(rays[base], rays[base + 1u], rays[base + 2u]);
            ray.TMin = rays[base + 3u];
            ray.Direction = float3(rays[base + 4u], rays[base + 5u], rays[base + 6u]);
            ray.TMax = rays[base + 7u];
            uint layers = asuint(rays[base + 8u]);
            uint queryLayer = asuint(rays[base + 9u]);
            RayQuery<RAY_FLAG_FORCE_NON_OPAQUE> query;
            query.TraceRayInline(scene, RAY_FLAG_FORCE_NON_OPAQUE, 255u, ray);
            float bestT = ray.TMax;
            uint bestPrimitive = 0xffffffffu;
            uint bestKind = 0u;
            float2 barycentrics = float2(0.0f, 0.0f);
            float3 normal = float3(0.0f, 0.0f, 0.0f);
            float frontFacing = 0.0f;
            // Do not commit while selecting candidates: this admits all equal-distance
            // candidates and makes stable primitive ordering independent of hardware traversal.
            while (query.Proceed()) {
                uint primitive = query.CandidatePrimitiveIndex();
                float t;
                uint layer;
                uint mask;
                uint kind;
                float2 candidateBary = float2(0.0f, 0.0f);
                float3 candidateNormal;
                if (query.CandidateType() == CANDIDATE_NON_OPAQUE_TRIANGLE) {
                    uint vertex = primitive * 12u;
                    float3 a = float3(triangles[vertex], triangles[vertex + 1u], triangles[vertex + 2u]);
                    float3 b = float3(triangles[vertex + 4u], triangles[vertex + 5u], triangles[vertex + 6u]);
                    float3 c = float3(triangles[vertex + 8u], triangles[vertex + 9u], triangles[vertex + 10u]);
                    t = query.CandidateTriangleRayT();
                    layer = asuint(triangles[vertex + 3u]);
                    mask = asuint(triangles[vertex + 7u]);
                    candidateNormal = normalize(cross(b - a, c - a));
                    candidateBary = query.CandidateTriangleBarycentrics();
                    kind = 1u;
                } else {
                    uint sphere = primitive * 8u;
                    float3 centre = float3(spheres[sphere], spheres[sphere + 1u], spheres[sphere + 2u]);
                    float radius = spheres[sphere + 3u];
                    float3 offset = ray.Origin - centre;
                    float b = dot(offset, ray.Direction);
                    float c = dot(offset, offset) - radius * radius;
                    float discriminant = b * b - c;
                    if (discriminant < 0.0f) continue;
                    float root = sqrt(discriminant);
                    float nearT = -b - root;
                    t = nearT >= ray.TMin ? nearT : -b + root;
                    layer = asuint(spheres[sphere + 4u]);
                    mask = asuint(spheres[sphere + 5u]);
                    candidateNormal = normalize(ray.Origin + t * ray.Direction - centre);
                    kind = 2u;
                }
                if ((layers & layer) == 0u || (queryLayer & mask) == 0u || t < ray.TMin || t > ray.TMax) continue;
                if (bestKind == 0u || t < bestT - 0.00001f || (abs(t - bestT) <= 0.00001f &&
                    (kind < bestKind || (kind == bestKind && primitive < bestPrimitive)))) {
                    bestT = t;
                    bestPrimitive = primitive;
                    bestKind = kind;
                    barycentrics = candidateBary;
                    normal = dot(candidateNormal, ray.Direction) > 0.0f ? -candidateNormal : candidateNormal;
                    frontFacing = dot(candidateNormal, ray.Direction) > 0.0f ? -1.0f : 1.0f;
                }
            }
            uint output = index * 16u;
            hits[output] = asfloat(bestKind);
            hits[output + 1u] = asfloat(bestPrimitive);
            // Numeric mirrors allow graphics shaders to consume the same packet
            // through an RGBA32F texture without interpreting uint bit patterns.
            hits[output + 2u] = float(bestKind);
            hits[output + 3u] = bestKind == 0u ? -1.0f : float(bestPrimitive);
            hits[output + 4u] = bestKind == 0u ? -1.0f : bestT;
            hits[output + 5u] = barycentrics.x;
            hits[output + 6u] = barycentrics.y;
            hits[output + 7u] = 0.0f;
            float3 hitPoint = ray.Origin + bestT * ray.Direction;
            hits[output + 8u] = hitPoint.x;
            hits[output + 9u] = hitPoint.y;
            hits[output + 10u] = hitPoint.z;
            hits[output + 11u] = 0.0f;
            hits[output + 12u] = normal.x;
            hits[output + 13u] = normal.y;
            hits[output + 14u] = normal.z;
            hits[output + 15u] = frontFacing;
        }
        """;
}
