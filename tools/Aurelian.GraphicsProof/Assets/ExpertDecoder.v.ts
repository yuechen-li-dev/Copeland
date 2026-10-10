import { PolynomialRadiance, NetworkRadiance, ResidualRadiance, GridPoint } from "./ExpertWeights";
import { Add3, Scale3 } from "./Lighting3D";

// Experimental profile: 32 fixed ReLU features, two RGB light bases, 6x6 coarse grid.
// Fitted coefficients specialize into closed VTS functions at scene compilation.
// This decoder imports no geometry or traversal.
function GridCell(coordinate: f32): u32 {
    var cell: u32 = 0;
    for (var index: u32 = 0; index < 6; index = index + 1) {
        if (coordinate >= Convert<f32>(index)) {
            cell = index;
        }
    }
    return cell;
}

function Grid(p: float2, channel: u32): float3 {
    let x: f32 = Clamp((p.x + 1.0) * 3.0 - 0.5, 0.0, 5.0);
    let z: f32 = Clamp((p.y + 1.0) * 3.0 - 0.5, 0.0, 5.0);
    let cellX: u32 = GridCell(x);
    let cellZ: u32 = GridCell(z);
    var right: u32 = cellX;
    var top: u32 = cellZ;
    if (right < 5) {
        right = right + 1;
    }
    if (top < 5) {
        top = top + 1;
    }
    let fx: f32 = x - Floor(x);
    let fz: f32 = z - Floor(z);
    let lower: float3 = Add3(Scale3(GridPoint(cellX, cellZ, channel), 1.0 - fx), Scale3(GridPoint(right, cellZ, channel), fx));
    let upper: float3 = Add3(Scale3(GridPoint(cellX, top, channel), 1.0 - fx), Scale3(GridPoint(right, top, channel), fx));
    return Add3(Scale3(lower, 1.0 - fz), Scale3(upper, fz));
}

export function PredictBasis(p: float2, mode: f32, channel: u32): float3 {
    var result: float3 = float3(0.0, 0.0, 0.0);
    if (mode < 0.5) {
        result = PolynomialRadiance(p, channel);
    } else {
        if (mode < 1.5) {
            result = Grid(p, channel);
        } else {
            if (mode < 2.5) {
                result = NetworkRadiance(p, channel);
            } else {
                result = Add3(Grid(p, channel), ResidualRadiance(p, channel));
            }
        }
    }
    return float3(Max(result.x, 0.0), Max(result.y, 0.0), Max(result.z, 0.0));
}

export function PredictLighting(p: float2, mode: f32, sun: f32, lamp: f32): float3 {
    return Add3(Scale3(PredictBasis(p, mode, 0), sun), Scale3(PredictBasis(p, mode, 3), lamp));
}
