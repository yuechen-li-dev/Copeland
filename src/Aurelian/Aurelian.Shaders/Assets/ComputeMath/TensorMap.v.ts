import { KernelParameters } from "./KernelMath";
import { BFloat16 } from "./BFloat16";

enum MapOperation {
    Round,
    Gather,
    Scatter,
    Modulate,
    GatedResidual,
    AddRounded,
    Rope3D,
    Append,
    ScatterFloat,
    ScaledResidual,
    Rope2D,
    Invalid
}

function DecodeMap(value: u32): MapOperation {
    if (value == 0) {
        return MapOperation.Round;
    }
    if (value == 1) {
        return MapOperation.Gather;
    }
    if (value == 2) {
        return MapOperation.Scatter;
    }
    if (value == 3) {
        return MapOperation.Modulate;
    }
    if (value == 4) {
        return MapOperation.GatedResidual;
    }
    if (value == 5) {
        return MapOperation.AddRounded;
    }
    if (value == 6) {
        return MapOperation.Rope3D;
    }
    if (value == 7) {
        return MapOperation.Append;
    }
    if (value == 8) {
        return MapOperation.ScatterFloat;
    }
    if (value == 9) {
        return MapOperation.ScaledResidual;
    }
    if (value == 10) {
        return MapOperation.Rope2D;
    }
    return MapOperation.Invalid;
}

@compute
@numthreads(64, 1, 1)
function TensorMapMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Auxiliary: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let index = thread.x + thread.y * p.stride;
    if (index >= p.rows * p.columns) {
        return;
    }
    let row = index / p.columns;
    let columnIndex = index % p.columns;
    match DecodeMap(p.mode) {
        case MapOperation.Round => {
            Output[index] = BFloat16(Input[index]);
        }
        case MapOperation.Gather => {
            Output[index] = Input[(row + p.offset) * p.inner + p.height + columnIndex];
        }
        case MapOperation.Scatter => {
            Output[(row + p.offset) * p.inner + p.height + columnIndex] = BFloat16(Input[index]);
        }
        case MapOperation.ScatterFloat => {
            Output[(row + p.offset) * p.inner + p.height + columnIndex] = Input[index];
        }
        case MapOperation.ScaledResidual => {
            Output[index] = Input[index] + Auxiliary[index] * Auxiliary[p.rows * p.columns + columnIndex];
        }
        case MapOperation.Rope2D => {
            if (row < p.offset) {
                Output[index] = Input[index];
            } else {
                let lane = columnIndex % p.width;
                let half = p.width / 2;
                let frequencyCount = p.width / 4;
                let frequency = lane % frequencyCount;
                let axis = lane / frequencyCount % 2;
                let token = row - p.offset;
                var coordinate = token / p.height;
                if (axis == 1) {
                    coordinate = token % p.height;
                }
                let position = (Convert<f32>(coordinate) + 0.5) / Convert<f32>(p.height) * 2.0 - 1.0;
                let phase = 6.283185307179586 * position / Pow(p.theta, Convert<f32>(frequency) / Convert<f32>(frequencyCount));
                let first = index - lane + lane % half;
                let a = Input[first];
                let b = Input[first + half];
                if (lane < half) {
                    Output[index] = a * Cos(phase) - b * Sin(phase);
                } else {
                    Output[index] = b * Cos(phase) + a * Sin(phase);
                }
            }
        }
        case MapOperation.Modulate => {
            let scale = BFloat16(1.0 + Auxiliary[p.offset + p.columns + columnIndex]);
            Output[index] = BFloat16(BFloat16(Input[index] * scale) + Auxiliary[p.offset + columnIndex]);
        }
        case MapOperation.GatedResidual => {
            Output[index] = BFloat16(Input[index] + BFloat16(Auxiliary[index] * Auxiliary[p.rows * p.columns + columnIndex]));
        }
        case MapOperation.AddRounded => {
            Output[index] = BFloat16(Input[index] + Auxiliary[index]);
        }
        case MapOperation.Append => {
            if (index < p.inner) {
                Output[index] = Input[index];
            } else if (index < p.inner + p.height) {
                Output[index] = Auxiliary[p.offset + index - p.inner];
            } else {
                Output[index] = 0.0;
            }
        }
        case MapOperation.Rope3D => {
            let pair = columnIndex / 2 % (p.width / 2);
            let frequencyCount = p.width / 2 / 3;
            var coordinate: u32 = 0;
            var phase: f32 = 0.0;
            if (pair < frequencyCount * 3) {
                let axis = pair / frequencyCount;
                if (axis == 0) {
                    coordinate = (row + p.offset) / (p.height * p.height);
                } else if (axis == 1) {
                    coordinate = (row + p.offset) / p.height % p.height;
                } else {
                    coordinate = (row + p.offset) % p.height;
                }
                phase = Convert<f32>(coordinate) / Pow(10000.0, Convert<f32>(pair % frequencyCount) / Convert<f32>(frequencyCount));
            }
            let evenIndex = index - columnIndex % 2;
            let a = Input[evenIndex];
            let b = Input[evenIndex + 1];
            if (columnIndex % 2 == 0) {
                Output[index] = BFloat16(a * Cos(phase) - b * Sin(phase));
            } else {
                Output[index] = BFloat16(a * Sin(phase) + b * Cos(phase));
            }
        }
        case MapOperation.Invalid => {
            Output[index] = Input[index];
        }
    }
}
