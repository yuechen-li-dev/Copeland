import { KernelParameters } from "./KernelMath";

// Single image NHWC -> [outputY*outputX, kernelY*kernelX*inputChannels].
// Signed coordinates are represented with an unsigned padding comparison.
@compute
@numthreads(64, 1, 1)
function Im2ColMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Auxiliary: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    if (thread.x >= p.rows * p.inner) {
        return;
    }
    let pixel = thread.x / p.inner;
    let component = thread.x % p.inner;
    let channel = component % p.channels;
    let kernel = component / p.channels;
    let paddedX = (pixel % p.columns) * p.stride + kernel % p.kernelWidth;
    let paddedY = (pixel / p.columns) * p.stride + kernel / p.kernelWidth;
    var value: f32 = 0.0;
    if (paddedX >= p.padding && paddedY >= p.padding) {
        let x = paddedX - p.padding;
        let y = paddedY - p.padding;
        if (x < p.width && y < p.height) {
            value = Input[(y * p.width + x) * p.channels + channel];
        }
    }
    Output[thread.x] = value;
}
