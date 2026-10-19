import { KernelParameters } from "./KernelMath";

// Cooperatively loaded 16×16 FP32 tiles. Eight statically expanded K tiles
// share one dispatch; all lanes participate in both barriers, including tails.
@compute
@numthreads(16, 16, 1)
function SgemmMain(@builtin(groupId) group: uint3,
    @builtin(groupThreadId) local: uint3,
    @workgroup readwrite TileA: WorkgroupArray<f32, 256>,
    @workgroup readwrite TileB: WorkgroupArray<f32, 256>,
    @binding(0) readonly A: StorageBuffer<f32>,
    @binding(1) readonly B: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let row = group.x * 16 + local.x;
    let columnIndex = group.y * 16 + local.y;
    var sum: f32 = 0.0;
    static for (let tile of Indices<8>()) {
        let aColumn = p.offset + tile * 16 + local.y;
        let bRow = p.offset + tile * 16 + local.x;
        var a: f32 = 0.0;
        var b: f32 = 0.0;
        if (row < p.rows && aColumn < p.inner) {
            a = A[row * p.inner + aColumn];
        }
        if (bRow < p.inner && columnIndex < p.columns) {
            if (p.mode == 1) {
                b = B[columnIndex * p.inner + bRow];
            } else {
                b = B[bRow * p.columns + columnIndex];
            }
        }
        TileA[local.x * 16 + local.y] = a;
        TileB[local.x * 16 + local.y] = b;
        WorkgroupBarrier();
        static for (let k of Indices<16>()) {
            sum = sum + TileA[local.x * 16 + k] * TileB[k * 16 + local.y];
        }
        WorkgroupBarrier();
    }
    if (row < p.rows && columnIndex < p.columns) {
        let index = row * p.columns + columnIndex;
        if (p.offset == 0) {
            // Avoid reading an uninitialized output when beta is zero.
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum;
        }
    }
}
