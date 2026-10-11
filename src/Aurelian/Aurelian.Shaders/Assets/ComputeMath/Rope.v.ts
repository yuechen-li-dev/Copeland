import { KernelParameters } from "./KernelMath";

// mode 0 = half-split (Gemma style); 1 = interleaved pairs.
@compute
@numthreads(64, 1, 1)
function RopeMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Auxiliary: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let half = p.columns / 2;
    if (thread.x >= p.rows * half) {
        return;
    }
    let row = thread.x / half;
    let pair = thread.x % half;
    var first = row * p.columns + pair;
    var second = first + half;
    if (p.mode == 1) {
        first = row * p.columns + pair * 2;
        second = first + 1;
    }
    let angle = Convert<f32>(row + p.offset) * Pow(p.theta, -2.0 * Convert<f32>(pair) / Convert<f32>(p.columns));
    let cosine = Cos(angle);
    let sine = Sin(angle);
    let x = Input[first];
    let y = Input[second];
    Output[first] = x * cosine - y * sine;
    Output[second] = x * sine + y * cosine;
}
