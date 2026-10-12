import { KernelParameters } from "./KernelMath";
import { BFloat16 } from "./BFloat16";

// Token coordinates are explicit XYZ values, not inferred lattice row numbers.
@compute
@numthreads(64, 1, 1)
function CoordinateRopeMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Coordinates: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let index = thread.x + thread.y * p.stride;
    if (index >= p.rows * p.columns) { return; }
    let row = index / p.columns;
    let columnIndex = index % p.columns;
    let pair = columnIndex / 2 % 64;
    var phase: f32 = 0.0;
    if (pair < 63) {
        let coordinate = Coordinates[row * 3 + pair / 21];
        phase = coordinate / Pow(10000.0, Convert<f32>(pair % 21) / 21.0);
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
