// Scalar hard-constraint mechanism probe of AVBD Eqs.8-12.
// Two masses with inertia 3600, load prediction .01, ground stiffness 10000.
// This is not a full cloth backend or a three-dimensional Hessian implementation.
@compute @numthreads(16, 1, 1)
function Main(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readwrite Output: StorageBuffer<f32>): void {
    if (thread.x >= 12) {
        return;
    }
    const index: u32 = thread.x * 3;
    const method: u32 = U32(Input[index]);
    const augmented: bool = method == 1;
    const budget: u32 = U32(Input[index + 1]);
    var x1: f32 = 0.01;
    var x2: f32 = 0.01;
    var lambda: f32 = 0.0;
    var groundMultiplier: f32 = 0.0;
    var couplingMultiplier: f32 = 0.0;
    var penalty: f32 = 3600.0;
    static for (let iteration of [
        0, 1, 2, 3, 4, 5, 6, 7,
        8, 9, 10, 11, 12, 13, 14, 15,
        16, 17, 18, 19, 20, 21, 22, 23,
        24, 25, 26, 27, 28, 29, 30, 31,
        32, 33, 34, 35, 36, 37, 38, 39,
        40, 41, 42, 43, 44, 45, 46, 47,
        48, 49, 50, 51, 52, 53, 54, 55,
        56, 57, 58, 59, 60, 61, 62, 63
    ]) {
        if (iteration < budget) {
            if (method == 2) {
                const groundIncrement: f32 = (-x1 - 0.36 * groundMultiplier) / 1.36;
                groundMultiplier = groundMultiplier + groundIncrement;
                x1 = x1 + groundIncrement;
                const couplingIncrement: f32 = (x2 - x1) * 0.5;
                couplingMultiplier = couplingMultiplier + couplingIncrement;
                x1 = x1 + couplingIncrement;
                x2 = x2 - couplingIncrement;
                lambda = -3600.0 * couplingMultiplier;
            } else {
                if (!augmented) {
                    penalty = 100000000.0;
                }
                x1 = (36.0 + penalty * x2 - lambda) / (13600.0 + penalty);
                x2 = (36.0 + penalty * x1 + lambda) / (3600.0 + penalty);
                if (augmented) {
                    const residual: f32 = x1 - x2;
                    lambda = lambda + penalty * residual;
                    penalty = penalty + 10.0 * Abs(residual);
                }
            }
        }
    }
    Output[index] = x1;
    Output[index + 1] = x2;
    Output[index + 2] = lambda;
}
