import { KernelParameters, Gelu } from "./KernelMath";

// BiasResidual accumulates into the existing residual tensor at binding 2.
@compute
@numthreads(64, 1, 1)
function ProjectionEpilogueMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Product: StorageBuffer<f32>,
    @binding(1) readonly Bias: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let index = thread.x + thread.y * p.stride;
    if (index < p.rows * p.columns) {
        let biased = Product[index] + Bias[index % p.columns];
        if (p.mode == 8) {
            Output[index] = Gelu(biased);
        } else {
            Output[index] = Output[index] + biased;
        }
    }
}
