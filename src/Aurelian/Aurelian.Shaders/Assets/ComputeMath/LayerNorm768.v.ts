import { KernelParameters } from "./KernelMath";

// Twelve FP32 values per lane, with the same centered variance and reduction tree as the general kernel.
@compute
@numthreads(64, 1, 1)
function LayerNorm768Main(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Sum: WorkgroupArray<f32, 64>,
    @workgroup readwrite Squares: WorkgroupArray<f32, 64>,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Auxiliary: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let row = group.x + group.y * p.height;
    let base = row * 768;
    var sum: f32 = 0.0;
    static for (let block of Indices<12>()) {
        let columnIndex = lane + block * 64;
        if (row < p.rows) {
            sum = sum + Input[base + columnIndex];
        }
    }
    Sum[lane] = sum;
    WorkgroupBarrier();
    static for (let stage of Indices<6>()) {
        let stride = 32 >> stage;
        if (lane < stride) {
            Sum[lane] = Sum[lane] + Sum[lane + stride];
        }
        WorkgroupBarrier();
    }
    let mean = Sum[0] / Convert<f32>(p.columns);
    var squares: f32 = 0.0;
    static for (let block of Indices<12>()) {
        let columnIndex = lane + block * 64;
        if (row < p.rows) {
            let centered = Input[base + columnIndex] - mean;
            squares = squares + centered * centered;
        }
    }
    Squares[lane] = squares;
    WorkgroupBarrier();
    static for (let stage of Indices<6>()) {
        let stride = 32 >> stage;
        if (lane < stride) {
            Squares[lane] = Squares[lane] + Squares[lane + stride];
        }
        WorkgroupBarrier();
    }
    let inverse = 1.0 / Sqrt(Squares[0] / Convert<f32>(p.columns) + p.epsilon);
    static for (let block of Indices<12>()) {
        let columnIndex = lane + block * 64;
        if (row < p.rows) {
            let bias = Auxiliary[768 + columnIndex];
            Output[base + columnIndex] = (Input[base + columnIndex] - mean) * inverse * Auxiliary[columnIndex] + bias;
        }
    }
}
