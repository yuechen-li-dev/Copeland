import { KernelParameters } from "./KernelMath";

// Prometheus radix-2 complex32 route: separate ping-pong stages, native twiddles.
@compute
@numthreads(64, 1, 1)
function ButterflyMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<float2>,
    @binding(1) readonly Auxiliary: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<float2>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    if (thread.x >= p.rows * p.columns) {
        return;
    }
    let batch = thread.x / p.columns;
    let element = thread.x % p.columns;
    let span = p.offset * 2;
    let within = element % span;
    let lower = within % p.offset;
    let base = batch * p.columns + (element / span) * span;
    let even = Input[base + lower];
    let odd = Input[base + lower + p.offset];
    var sign: f32 = -1.0;
    if (p.mode == 1) {
        sign = 1.0;
    }
    let angle = sign * 6.283185307179586 * Convert<f32>(lower) / Convert<f32>(span);
    let cosine = Cos(angle);
    let sine = Sin(angle);
    let real = cosine * odd.x - sine * odd.y;
    let imaginary = sine * odd.x + cosine * odd.y;
    var value = float2(even.x - real, even.y - imaginary);
    if (within < p.offset) {
        value = float2(even.x + real, even.y + imaginary);
    }
    Output[thread.x] = value * p.alpha;
}
