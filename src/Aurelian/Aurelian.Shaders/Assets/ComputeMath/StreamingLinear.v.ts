import { KernelParameters } from "./KernelMath";

// One uploaded output-row tile is reused until all of its K slices finish.
// p.width is the full output stride, p.height is the tile's first output column.
// Weight rows are inputWidth + 1 wide; their final value holds affine bias.
@compute
@numthreads(16, 16, 1)
function StreamingLinearMain(@builtin(groupId) group: uint3,
    @builtin(groupThreadId) local: uint3,
    @workgroup readwrite TileA: WorkgroupArray<f32, 256>,
    @workgroup readwrite TileB: WorkgroupArray<f32, 256>,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Weights: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let row = group.x * 16 + local.x;
    let columnIndex = group.y * 16 + local.y;
    let aColumn = p.offset + local.y;
    let bRow = p.offset + local.x;
    var a: f32 = 0.0;
    var b: f32 = 0.0;
    if (row < p.rows && aColumn < p.inner) {
        a = Input[row * p.inner + aColumn];
    }
    if (columnIndex < p.columns && bRow < p.inner) {
        b = Weights[columnIndex * (p.inner + 1) + bRow];
    }
    TileA[local.x * 16 + local.y] = a;
    TileB[local.x * 16 + local.y] = b;
    WorkgroupBarrier();
    var sum: f32 = 0.0;
    static for (let k of Indices<16>()) {
        sum = sum + TileA[local.x * 16 + k] * TileB[k * 16 + local.y];
    }
    if (row < p.rows && columnIndex < p.columns) {
        let index = row * p.width + p.height + columnIndex;
        if (p.offset == 0) {
            Output[index] = sum;
        } else {
            Output[index] = Output[index] + sum;
        }
        if (p.offset + 16 >= p.inner) {
            Output[index] = Output[index] + Weights[columnIndex * (p.inner + 1) + p.inner];
        }
    }
}
