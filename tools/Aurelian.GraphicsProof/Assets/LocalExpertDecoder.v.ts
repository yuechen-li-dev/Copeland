import { GeometryRadiance, UniformRadiance, LocalGridPoint } from "./LocalExpertWeights";
import { Add3, Scale3 } from "./Lighting3D";

function GridCell(coordinate: f32): u32 {
    var cell: u32 = 0;
    for (var index: u32 = 0; index < 10; index = index + 1) {
        if (coordinate >= Convert<f32>(index)) {
            cell = index;
        }
    }
    return cell;
}

function Grid(p: float2, channel: u32): float3 {
    let x: f32 = Clamp((p.x + 1.0) * 5.0 - 0.5, 0.0, 9.0);
    let z: f32 = Clamp((p.y + 1.0) * 5.0 - 0.5, 0.0, 9.0);
    let cellX: u32 = GridCell(x);
    let cellZ: u32 = GridCell(z);
    var right: u32 = cellX;
    var top: u32 = cellZ;
    if (right < 9) {
        right = right + 1;
    }
    if (top < 9) {
        top = top + 1;
    }
    let fx: f32 = x - Floor(x);
    let fz: f32 = z - Floor(z);
    let lower: float3 = Add3(Scale3(LocalGridPoint(cellX, cellZ, channel), 1.0 - fx), Scale3(LocalGridPoint(right, cellZ, channel), fx));
    let upper: float3 = Add3(Scale3(LocalGridPoint(cellX, top, channel), 1.0 - fx), Scale3(LocalGridPoint(right, top, channel), fx));
    return Add3(Scale3(lower, 1.0 - fz), Scale3(upper, fz));
}

export function PredictBasis(p: float2, mode: f32, channel: u32): float3 {
    var result: float3 = float3(0.0, 0.0, 0.0);
    if (mode < 4.5) {
        result = GeometryRadiance(p, channel);
    } else {
        if (mode < 5.5) {
            result = UniformRadiance(p, channel);
        } else {
            result = Grid(p, channel);
        }
    }
    return float3(Max(result.x, 0.0), Max(result.y, 0.0), Max(result.z, 0.0));
}

export function PredictLighting(p: float2, mode: f32, sun: f32, lamp: f32): float3 {
    return Add3(Scale3(PredictBasis(p, mode, 0), sun), Scale3(PredictBasis(p, mode, 3), lamp));
}
