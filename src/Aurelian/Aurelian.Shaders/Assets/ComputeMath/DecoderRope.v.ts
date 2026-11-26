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
    if (p.mode == 2) {
        frequency = frequency / Auxiliary[pair];
    }
    let angle = Convert<f32>(token + p.offset) * frequency;
    let cosine = Cos(angle);
    let sine = Sin(angle);
    let first = head * p.width + pair;
    let second = first + half;
    let x = Input[first];
    let y = Input[second];
    if (p.mode == 0) {
        let roundedCos = BFloat16(cosine);
        let roundedSin = BFloat16(sine);
        Output[first] = BFloat16(BFloat16(x * roundedCos) - BFloat16(y * roundedSin));
        Output[second] = BFloat16(BFloat16(y * roundedCos) + BFloat16(x * roundedSin));
    } else {
        Output[first] = x * cosine - y * sine;
        Output[second] = y * cosine + x * sine;
    }
}
