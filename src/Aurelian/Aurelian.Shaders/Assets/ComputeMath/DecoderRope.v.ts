import { KernelParameters } from "./KernelMath";
import { BFloat16 } from "./BFloat16";

@compute
@numthreads(64, 1, 1)
function DecoderRopeMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Auxiliary: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    if (thread.x >= p.rows * p.columns / 2) { return; }
    let half = p.width / 2;
    let pair = thread.x % half;
    let head = thread.x / half;
    let token = head / (p.columns / p.width);
    var frequency: f32 = 0.0;
    if (pair < p.height) {
        frequency = Pow(p.theta, -2.0 * Convert<f32>(pair) / Convert<f32>(p.width));
    }
    let angle = Convert<f32>(token + p.offset) * frequency;
    let cosine = BFloat16(Cos(angle));
    let sine = BFloat16(Sin(angle));
    let first = head * p.width + pair;
    let second = first + half;
    let x = Input[first];
    let y = Input[second];
    Output[first] = BFloat16(BFloat16(x * cosine) - BFloat16(y * sine));
    Output[second] = BFloat16(BFloat16(y * cosine) + BFloat16(x * sine));
}
