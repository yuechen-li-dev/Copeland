import { KernelParameters } from "./KernelMath";

// A full-K resident 64x64 tile with a 4x4 register tile per lane. Host admission
// proves K <= 8192, so the uniform bounded loop covers every 16-wide K tile.
// Adjacent lanes load contiguous input columns. All lanes reach both barriers.
@compute
@numthreads(16, 16, 1)
function ResidentSgemmMain(@builtin(groupId) group: uint3,
    @builtin(groupThreadId) local: uint3,
    @workgroup readwrite TileA: WorkgroupArray<f32, 1024>,
    @workgroup readwrite TileB: WorkgroupArray<f32, 1024>,
    @binding(0) readonly A: StorageBuffer<f32>,
    @binding(1) readonly B: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let tileRow = group.x * 64;
    let tileColumn = group.y * 64;
    let lane = local.y * 16 + local.x;
    var sums: Array<f32, 16> = [
        0.0, 0.0, 0.0, 0.0,
        0.0, 0.0, 0.0, 0.0,
        0.0, 0.0, 0.0, 0.0,
        0.0, 0.0, 0.0, 0.0,
    ];

    for (let tile of BoundedIndices<512>((p.inner + 15) / 16)) {
        static for (let part of Indices<4>()) {
            let index = lane + part * 256;
            let aRow = tileRow + index / 16;
            let aColumn = p.offset + tile * 16 + index % 16;
            let bRow = p.offset + tile * 16 + index / 64;
            let bColumn = tileColumn + index % 64;
            var a: f32 = 0.0;
            var b: f32 = 0.0;
            if (aRow < p.rows && aColumn < p.inner) {
                a = A[aRow * p.inner + aColumn];
            }
            if (bRow < p.inner && bColumn < p.columns) {
                if (p.mode == 1) {
                    b = B[bColumn * p.inner + bRow];
                } else {
                    b = B[bRow * p.columns + bColumn];
                }
            }
            TileA[index] = a;
            TileB[index] = b;
        }
        WorkgroupBarrier();

        static for (let k of Indices<16>()) {
            // Load each operand once, then reuse it across the 4x4 outer product.
            var left: Array<f32, 4> = [0.0, 0.0, 0.0, 0.0];
            var right: Array<f32, 4> = [0.0, 0.0, 0.0, 0.0];
            static for (let slot of Indices<4>()) {
                left[slot] = TileA[(local.y + slot * 16) * 16 + k];
                right[slot] = TileB[k * 64 + local.x + slot * 16];
            }
            static for (let rowSlot of Indices<4>()) {
                let a = left[rowSlot];
                static for (let columnSlot of Indices<4>()) {
                    const cell = rowSlot * 4 + columnSlot;
                    sums[cell] = sums[cell] + a * right[columnSlot];
                }
            }
        }
        WorkgroupBarrier();
    }

    static for (let rowSlot of Indices<4>()) {
        let outputRow = tileRow + local.y + rowSlot * 16;
        static for (let columnSlot of Indices<4>()) {
            let outputColumn = tileColumn + local.x + columnSlot * 16;
            if (outputRow < p.rows && outputColumn < p.columns) {
                const cell = rowSlot * 4 + columnSlot;
                let index = outputRow * p.columns + outputColumn;
                let scaled = p.alpha * sums[cell];
                if (p.offset == 0) {
                    var prior: f32 = 0.0;
                    if (p.beta != 0.0) {
                        prior = Output[index] * p.beta;
                    }
                    Output[index] = scaled + prior;
                } else {
                    Output[index] = Output[index] + scaled;
                }
            }
        }
    }
}
