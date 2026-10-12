import { KernelParameters } from "./KernelMath";

// mode 0 = RMSNorm; 1 = LayerNorm. Auxiliary stores gamma followed by beta.
@compute
@numthreads(64, 1, 1)
function NormalizationMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Sum: WorkgroupArray<f32, 64>,
    @workgroup readwrite Squares: WorkgroupArray<f32, 64>,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Auxiliary: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let row = group.x + group.y * p.height;
    let base = row * p.columns;
    var sum: f32 = 0.0;
    static for (let block of Indices<128>()) {
        let columnIndex = lane + block * 64;
        if (columnIndex < p.columns && row < p.rows) {
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
    var mean: f32 = 0.0;
    if (p.mode == 1) {
        mean = Sum[0] / Convert<f32>(p.columns);
    }
    var squares: f32 = 0.0;
    static for (let block of Indices<128>()) {
        let columnIndex = lane + block * 64;
        if (columnIndex < p.columns && row < p.rows) {
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
    static for (let block of Indices<128>()) {
        let columnIndex = lane + block * 64;
        if (columnIndex < p.columns && row < p.rows) {
            var bias: f32 = 0.0;
            if (p.mode == 1) {
                bias = Auxiliary[p.columns + columnIndex];
            }
            Output[base + columnIndex] = (Input[base + columnIndex] - mean) * inverse * Auxiliary[columnIndex] + bias;
        }
    }
}
