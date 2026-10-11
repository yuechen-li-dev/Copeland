import { Add3, Scale3, Mul3 } from "./Lighting3D";

function DataPixel(data: Texture2D<float4>, sampler: Sampler, index: f32, height: f32): float4 {
    let texelY: f32 = Floor(index / 32.0);
    let texelX: f32 = index - texelY * 32.0;
    return Sample(data, sampler, float2((texelX + 0.5) / 32.0, (texelY + 0.5) / height));
}

function WeightedNode(data: Texture2D<float4>, sampler: Sampler, index: f32,
    basis: u32, start: f32, height: f32, weight: f32): float3 {
    let value: float4 = DataPixel(data, sampler, start + index * 2.0 + Convert<f32>(basis), height);
    return Scale3(float3(value.x, value.y, value.z), weight);
}

export function CompiledDiffuse(data: Texture2D<float4>, sampler: Sampler, world: float3,
    normal: float3, sun: f32, emission: f32, sunColour: float3): float4 {
    // Header reads use row zero; the host uses a nearest sampler with clamp-to-edge addressing.
    let domain: float4 = Sample(data, sampler, float2(0.5 / 32.0, 0.0));
    let header: float4 = Sample(data, sampler, float2(1.5 / 32.0, 0.0));
    let p: float2 = float2((world.x - domain.x) * domain.z - 1.0, (world.z - domain.y) * domain.w - 1.0);
    if (normal.y < 0.999 || Abs(world.y - header.x) >= 0.001 || Abs(p.x) > 1.0 || Abs(p.y) > 1.0) {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    for (var elementIndex: u32 = 0; elementIndex < 128; elementIndex = elementIndex + 1) {
        if (Convert<f32>(elementIndex) < header.y) {
            let offset: f32 = 2.0 + Convert<f32>(elementIndex) * 5.0;
            let rowA: float4 = DataPixel(data, sampler, offset, header.w);
            let rowB: float4 = DataPixel(data, sampler, offset + 1.0, header.w);
            let rowC: float4 = DataPixel(data, sampler, offset + 2.0, header.w);
            let a: f32 = p.x * rowA.x + p.y * rowA.y + rowA.z;
            let b: f32 = p.x * rowB.x + p.y * rowB.y + rowB.z;
            let c: f32 = p.x * rowC.x + p.y * rowC.y + rowC.z;
            if (a >= -0.000001 && b >= -0.000001 && c >= -0.000001) {
                let idsA: float4 = DataPixel(data, sampler, offset + 3.0, header.w);
                let idsB: float4 = DataPixel(data, sampler, offset + 4.0, header.w);
                var result: float3 = float3(0.0, 0.0, 0.0);
                for (var basis: u32 = 0; basis < 2; basis = basis + 1) {
                    var response: float3 = WeightedNode(data, sampler, idsA.x, basis, header.z, header.w, a * (2.0 * a - 1.0));
                    response = Add3(response, WeightedNode(data, sampler, idsA.y, basis, header.z, header.w, b * (2.0 * b - 1.0)));
                    response = Add3(response, WeightedNode(data, sampler, idsA.z, basis, header.z, header.w, c * (2.0 * c - 1.0)));
                    response = Add3(response, WeightedNode(data, sampler, idsA.w, basis, header.z, header.w, 4.0 * a * b));
                    response = Add3(response, WeightedNode(data, sampler, idsB.x, basis, header.z, header.w, 4.0 * b * c));
                    response = Add3(response, WeightedNode(data, sampler, idsB.y, basis, header.z, header.w, 4.0 * c * a));
                    response = float3(Max(response.x, 0.0), Max(response.y, 0.0), Max(response.z, 0.0));
                    if (basis == 0) {
                        result = Add3(result, Scale3(Mul3(response, sunColour), sun));
                    } else {
                        result = Add3(result, Scale3(response, emission));
                    }
                }
                return float4(result.x, result.y, result.z, 1.0);
            }
        }
    }
    return float4(0.0, 0.0, 0.0, 0.0);
}
